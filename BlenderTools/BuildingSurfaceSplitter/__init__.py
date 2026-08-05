bl_info = {
    "name": "Bento Building Surface Splitter",
    "author": "Bento",
    "version": (0, 1, 0),
    "blender": (4, 2, 0),
    "location": "View3D > Sidebar > Bento",
    "description": "Classify building mesh faces into interior and exterior with interior volumes",
    "category": "Mesh",
}

import bpy
import bmesh
from bpy.props import BoolProperty, EnumProperty, FloatProperty, PointerProperty, StringProperty
from bpy.types import Operator, Panel, PropertyGroup
from mathutils import Vector
from mathutils.bvhtree import BVHTree


CLASS_ATTRIBUTE = "bento_surface_class"
CLASS_UNCLASSIFIED = 0
CLASS_INTERIOR = 1
CLASS_EXTERIOR = 2
CLASS_EXCLUDED = 3
FLOOR_NAME_HINTS = ("地板", "地面", "floor", "ground")


def is_mesh_object(obj):
    return obj is not None and obj.type == "MESH"


def is_volume_object(obj):
    return is_mesh_object(obj) and bool(obj.get("bento_interior_volume"))


def is_excluded_object(obj):
    return bool(obj and obj.get("bento_surface_excluded"))


def is_output_object(obj):
    return bool(obj and obj.get("bento_surface_output"))


def is_floor_object(obj):
    name = obj.name.casefold()
    return any(hint in name for hint in FLOOR_NAME_HINTS)


def is_target_object(obj):
    return bool(obj and obj.get("bento_surface_target"))


def iter_target_objects(context):
    settings = context.scene.bento_surface_splitter
    if settings.target_scope == "IMPORTED":
        candidates = [obj for obj in context.scene.objects if is_target_object(obj)]
    elif settings.target_scope == "SELECTED":
        candidates = list(context.selected_objects)
    else:
        candidates = list(context.scene.objects)
    return [
        obj for obj in candidates
        if is_mesh_object(obj)
        and not is_volume_object(obj)
        and not is_excluded_object(obj)
        and not is_output_object(obj)
    ]


def iter_volumes(context):
    return [obj for obj in context.scene.objects if is_volume_object(obj)]


def ensure_attribute(mesh):
    attribute = mesh.attributes.get(CLASS_ATTRIBUTE)
    if attribute and (attribute.domain != "FACE" or attribute.data_type != "INT"):
        mesh.attributes.remove(attribute)
        attribute = None
    return attribute or mesh.attributes.new(CLASS_ATTRIBUTE, "INT", "FACE")


def point_inside_volume(world_point, volume_obj, tolerance):
    local = volume_obj.matrix_world.inverted_safe() @ world_point
    half = Vector((0.5 + tolerance, 0.5 + tolerance, 0.5 + tolerance))
    return (
        -half.x <= local.x <= half.x
        and -half.y <= local.y <= half.y
        and -half.z <= local.z <= half.z
    )


def point_inside_volume_horizontal(world_point, volume_obj, tolerance):
    local = volume_obj.matrix_world.inverted_safe() @ world_point
    half = 0.5 + tolerance
    return -half <= local.x <= half and -half <= local.y <= half


def point_matches_interior_floor(world_point, volume_obj, tolerance):
    local = volume_obj.matrix_world.inverted_safe() @ world_point
    half = 0.5 + tolerance
    return (
        -half <= local.x <= half
        and -half <= local.y <= half
        and local.z <= half + tolerance
    )


def ray_distance_to_volume(world_origin, world_direction, volume_obj, tolerance):
    inverse = volume_obj.matrix_world.inverted_safe()
    local_origin = inverse @ world_origin
    local_direction = inverse.to_3x3() @ world_direction
    bounds_min = Vector((-0.5 - tolerance,) * 3)
    bounds_max = Vector((0.5 + tolerance,) * 3)
    distance_min = float("-inf")
    distance_max = float("inf")

    for axis in range(3):
        origin = local_origin[axis]
        direction = local_direction[axis]
        if abs(direction) < 1e-8:
            if origin < bounds_min[axis] or origin > bounds_max[axis]:
                return None
            continue
        first = (bounds_min[axis] - origin) / direction
        second = (bounds_max[axis] - origin) / direction
        if first > second:
            first, second = second, first
        distance_min = max(distance_min, first)
        distance_max = min(distance_max, second)
        if distance_min > distance_max:
            return None

    if distance_max < 0.0:
        return None
    return max(distance_min, 0.0)


def build_target_bvh(context, targets):
    depsgraph = context.evaluated_depsgraph_get()
    vertices = []
    polygons = []
    face_sources = []
    for obj in targets:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        offset = len(vertices)
        vertices.extend(evaluated.matrix_world @ vertex.co for vertex in mesh.vertices)
        for polygon in mesh.polygons:
            polygons.append([offset + vertex_index for vertex_index in polygon.vertices])
            face_sources.append(obj)
        evaluated.to_mesh_clear()
    return (BVHTree.FromPolygons(vertices, polygons, all_triangles=False), face_sources) if polygons else (None, [])


def calculate_adaptive_search_distance(targets, volumes, minimum):
    points = []
    for obj in [*targets, *volumes]:
        points.extend(obj.matrix_world @ Vector(corner) for corner in obj.bound_box)
    if not points:
        return minimum
    bounds_min = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    bounds_max = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    return max(minimum, (bounds_max - bounds_min).length)


def is_top_exposed_surface(world_center, target_bvh, settings, search_distance):
    if target_bvh is None:
        return False
    epsilon = max(settings.volume_tolerance, 0.0005)
    origin = world_center + Vector((0.0, 0.0, epsilon * 4.0))
    hit_location = target_bvh.ray_cast(
        origin,
        Vector((0.0, 0.0, 1.0)),
        search_distance,
    )[0]
    return hit_location is None


def volume_sample_points(volume):
    points = []
    for x in (-0.35, 0.0, 0.35):
        for y in (-0.35, 0.0, 0.35):
            for z in (-0.48, -0.25, 0.0, 0.25, 0.48):
                points.append(volume.matrix_world @ Vector((x, y, z)))
    return points


def propagate_interior_to_adjacent_faces(obj, class_values, settings):
    mesh = obj.data
    edge_faces = {}
    for polygon in mesh.polygons:
        for edge_key in polygon.edge_keys:
            edge_faces.setdefault(edge_key, []).append(polygon.index)

    normal_matrix = obj.matrix_world.to_3x3().inverted_safe().transposed()
    world_normals = [
        (normal_matrix @ polygon.normal).normalized()
        for polygon in mesh.polygons
    ]
    changed = True
    while changed:
        changed = False
        for polygon in mesh.polygons:
            index = polygon.index
            if class_values[index] != CLASS_EXTERIOR:
                continue
            normal = world_normals[index]
            if abs(normal.z) >= settings.upward_normal_threshold:
                continue
            neighbors = {
                neighbor
                for edge_key in polygon.edge_keys
                for neighbor in edge_faces.get(edge_key, ())
                if neighbor != index
            }
            if any(
                class_values[neighbor] == CLASS_INTERIOR
                and world_normals[neighbor].dot(normal) >= 0.92
                for neighbor in neighbors
            ):
                class_values[index] = CLASS_INTERIOR
                changed = True


def polygon_world_geometry(obj, polygon, normal_matrix):
    vertices = [obj.matrix_world @ obj.data.vertices[index].co for index in polygon.vertices]
    center = obj.matrix_world @ polygon.center
    normal = (normal_matrix @ polygon.normal).normalized()
    radius = max(((vertex - center).length for vertex in vertices), default=0.0)
    min_z = min((vertex.z for vertex in vertices), default=center.z)
    max_z = max((vertex.z for vertex in vertices), default=center.z)
    return center, normal, radius, min_z, max_z


def propagate_interior_to_coplanar_faces(obj, class_values, volumes, settings):
    normal_matrix = obj.matrix_world.to_3x3().inverted_safe().transposed()
    geometry = [
        polygon_world_geometry(obj, polygon, normal_matrix)
        for polygon in obj.data.polygons
    ]
    interior_seeds = [
        index
        for index, value in enumerate(class_values)
        if value == CLASS_INTERIOR
        and abs(geometry[index][1].z) < settings.upward_normal_threshold
    ]
    if not interior_seeds:
        return

    tolerance = max(settings.volume_tolerance, 0.0005)
    for index, value in enumerate(class_values):
        if value != CLASS_EXTERIOR:
            continue
        center, normal, radius, min_z, max_z = geometry[index]
        if abs(normal.z) >= settings.upward_normal_threshold:
            continue
        if not any(
            point_inside_volume(point, volume, tolerance * 4.0)
            for volume in volumes
            for point in polygon_sample_points(obj, obj.data.polygons[index])
        ):
            continue
        for seed_index in interior_seeds:
            seed_center, seed_normal, seed_radius, seed_min_z, seed_max_z = geometry[seed_index]
            if seed_normal.dot(normal) < 0.965:
                continue
            plane_distance = abs((center - seed_center).dot(seed_normal))
            if plane_distance > max(tolerance * 8.0, (radius + seed_radius) * 0.06):
                continue
            lateral = center - seed_center
            lateral -= seed_normal * lateral.dot(seed_normal)
            lateral.z = 0.0
            z_gap = max(0.0, min_z - seed_max_z, seed_min_z - max_z)
            if lateral.length <= radius + seed_radius + tolerance * 12.0 and z_gap <= tolerance * 12.0:
                class_values[index] = CLASS_INTERIOR
                break


def polygon_sample_points(obj, polygon):
    world_vertices = [obj.matrix_world @ obj.data.vertices[index].co for index in polygon.vertices]
    center = obj.matrix_world @ polygon.center
    points = [center]
    for vertex in world_vertices:
        points.append(center.lerp(vertex, 0.65))
    if len(world_vertices) >= 4:
        for index, vertex in enumerate(world_vertices):
            next_vertex = world_vertices[(index + 1) % len(world_vertices)]
            edge_midpoint = (vertex + next_vertex) * 0.5
            points.append(center.lerp(edge_midpoint, 0.65))
    return points


def is_visible_from_volume(surface_points, volume_samples, target_bvh, settings, search_distance):
    if target_bvh is None:
        return False
    max_distance = search_distance
    surface_epsilon = max(settings.volume_tolerance, 0.0005)
    for surface_point in surface_points:
        for origin in volume_samples:
            direction = surface_point - origin
            distance = direction.length
            if distance <= surface_epsilon:
                return True
            if distance > max_distance:
                continue
            direction.normalize()
            hit = target_bvh.ray_cast(origin, direction, distance + surface_epsilon)
            hit_location = hit[0]
            if hit_location is not None and (hit_location - surface_point).length <= surface_epsilon * 4.0:
                return True
    return False


def classify_polygon(obj, polygon, volumes, volume_samples, settings, target_bvh, search_distance):
    world_center = obj.matrix_world @ polygon.center
    normal_matrix = obj.matrix_world.to_3x3().inverted_safe().transposed()
    world_normal = (normal_matrix @ polygon.normal).normalized()
    if world_normal.z >= settings.upward_normal_threshold:
        if is_floor_object(obj):
            return CLASS_INTERIOR
        if is_top_exposed_surface(world_center, target_bvh, settings, search_distance):
            return CLASS_INTERIOR
        return (
            CLASS_INTERIOR
            if any(
                point_matches_interior_floor(point, volume, settings.volume_tolerance)
                for volume in volumes
                for point in polygon_sample_points(obj, polygon)
            )
            else CLASS_EXTERIOR
        )
    if world_normal.z <= -settings.upward_normal_threshold:
        return CLASS_EXTERIOR

    if any(point_inside_volume(world_center, volume, settings.volume_tolerance) for volume in volumes):
        return CLASS_INTERIOR
    if is_visible_from_volume(
        polygon_sample_points(obj, polygon),
        volume_samples,
        target_bvh,
        settings,
        search_distance,
    ):
        return CLASS_INTERIOR
    return CLASS_EXTERIOR


def assign_selected_faces(context, class_value):
    changed = 0
    for obj in context.objects_in_mode_unique_data:
        if not is_mesh_object(obj):
            continue
        bm = bmesh.from_edit_mesh(obj.data)
        bm.faces.ensure_lookup_table()
        layer = bm.faces.layers.int.get(CLASS_ATTRIBUTE) or bm.faces.layers.int.new(CLASS_ATTRIBUTE)
        for face in bm.faces:
            if face.select:
                face[layer] = class_value
                changed += 1
        bmesh.update_edit_mesh(obj.data, loop_triangles=False, destructive=False)
    return changed


def overwrite_class_from_selection(context, class_value, fallback_value):
    changed = 0
    for obj in context.objects_in_mode_unique_data:
        if not is_mesh_object(obj):
            continue
        bm = bmesh.from_edit_mesh(obj.data)
        bm.faces.ensure_lookup_table()
        layer = bm.faces.layers.int.get(CLASS_ATTRIBUTE) or bm.faces.layers.int.new(CLASS_ATTRIBUTE)
        for face in bm.faces:
            next_value = class_value if face.select else (
                fallback_value if face[layer] == class_value else face[layer]
            )
            if face[layer] != next_value:
                face[layer] = next_value
                changed += 1
        bmesh.update_edit_mesh(obj.data, loop_triangles=False, destructive=False)
    return changed


def select_class_faces(context, class_value):
    targets = iter_target_objects(context)
    if not targets:
        raise RuntimeError("没有可检查的网格对象。")
    if context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")

    bpy.ops.object.select_all(action="DESELECT")
    for obj in targets:
        obj.select_set(True)
    context.view_layer.objects.active = targets[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")
    bpy.ops.mesh.select_all(action="DESELECT")

    for obj in context.objects_in_mode_unique_data:
        bm = bmesh.from_edit_mesh(obj.data)
        bm.faces.ensure_lookup_table()
        layer = bm.faces.layers.int.get(CLASS_ATTRIBUTE)
        for face in bm.faces:
            face.select = layer is not None and face[layer] == class_value
        bmesh.update_edit_mesh(obj.data, loop_triangles=False, destructive=False)


def copy_polygon_subset(source_obj, class_value, output_name):
    source_mesh = source_obj.data
    attribute = source_mesh.attributes.get(CLASS_ATTRIBUTE)
    if attribute is None:
        return None
    selected = [polygon for polygon in source_mesh.polygons if attribute.data[polygon.index].value == class_value]
    if not selected:
        return None

    vertex_indices = sorted({index for polygon in selected for index in polygon.vertices})
    remap = {old: new for new, old in enumerate(vertex_indices)}
    vertices = [source_mesh.vertices[index].co.copy() for index in vertex_indices]
    faces = [[remap[index] for index in polygon.vertices] for polygon in selected]

    mesh = bpy.data.meshes.new(output_name)
    mesh.from_pydata(vertices, [], faces)
    for material in source_mesh.materials:
        mesh.materials.append(material)
    for new_polygon, source_polygon in zip(mesh.polygons, selected):
        new_polygon.material_index = source_polygon.material_index
        new_polygon.use_smooth = source_polygon.use_smooth
    for source_layer in source_mesh.uv_layers:
        target_layer = mesh.uv_layers.new(name=source_layer.name)
        for new_polygon, source_polygon in zip(mesh.polygons, selected):
            for new_loop, source_loop_index in zip(new_polygon.loop_indices, source_polygon.loop_indices):
                target_layer.data[new_loop].uv = source_layer.data[source_loop_index].uv
    mesh.update()

    result = bpy.data.objects.new(output_name, mesh)
    result.matrix_world = source_obj.matrix_world.copy()
    result["bento_surface_output"] = True
    return result


def get_or_create_child_collection(parent, name):
    for collection in parent.children:
        if collection.name == name:
            return collection
    collection = bpy.data.collections.new(name)
    parent.children.link(collection)
    return collection


def ensure_parent_empty(context, name):
    obj = bpy.data.objects.get(name)
    if obj is None:
        obj = bpy.data.objects.new(name, None)
        context.scene.collection.objects.link(obj)
    obj["bento_surface_output"] = True
    return obj


class BentoSurfaceSplitterSettings(PropertyGroup):
    target_scope: EnumProperty(
        name="处理范围",
        items=[
            ("IMPORTED", "Unity 导入模型", "自动处理本次从 Unity 导入的全部房屋网格，不受当前选择影响"),
            ("SELECTED", "选中对象", "只处理当前选中的网格对象"),
            ("SCENE", "整个场景", "处理场景中未排除的网格对象"),
        ],
        default="IMPORTED",
    )
    upward_normal_threshold: FloatProperty(
        name="向上阈值",
        description="世界法线 Z 分量达到该值时强制归为室内；0.707 约等于 45 度",
        default=0.70710678,
        min=0.0,
        max=1.0,
        precision=3,
    )
    surface_probe_distance: FloatProperty(
        name="室内搜索距离",
        description="从表面沿法线两侧寻找室内体积的最大世界空间距离",
        default=5.0,
        min=0.0001,
        soft_max=20.0,
        unit="LENGTH",
    )
    volume_tolerance: FloatProperty(
        name="体积容差",
        description="室内方块局部空间的额外容差",
        default=0.001,
        min=0.0,
        soft_max=0.1,
    )
    interior_name: StringProperty(name="室内节点", default="Interior")
    exterior_name: StringProperty(name="外墙节点", default="Exterior")
    roof_name: StringProperty(name="屋顶节点", default="Roof")
    export_path: StringProperty(name="FBX 输出", subtype="FILE_PATH")
    export_after_split: BoolProperty(name="分离后立即导出", default=False)


class Bento_OT_create_interior_volume(Operator):
    bl_idname = "bento.create_interior_volume"
    bl_label = "创建室内方块"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        bpy.ops.mesh.primitive_cube_add()
        obj = context.active_object
        obj.name = "Bento_InteriorVolume"
        obj["bento_interior_volume"] = True
        obj.display_type = "WIRE"
        obj.color = (0.1, 0.45, 1.0, 0.35)
        obj.show_in_front = True
        return {"FINISHED"}


class Bento_OT_mark_volume(Operator):
    bl_idname = "bento.mark_interior_volume"
    bl_label = "把选中对象设为室内方块"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        count = 0
        for obj in context.selected_objects:
            if not is_mesh_object(obj):
                continue
            obj["bento_interior_volume"] = True
            obj["bento_surface_excluded"] = False
            obj.display_type = "WIRE"
            obj.show_in_front = True
            count += 1
        self.report({"INFO"}, f"已设置 {count} 个室内方块")
        return {"FINISHED"}


class Bento_OT_toggle_excluded(Operator):
    bl_idname = "bento.toggle_surface_excluded"
    bl_label = "切换选中对象排除状态"
    bl_options = {"REGISTER", "UNDO"}

    excluded: BoolProperty(default=True)

    def execute(self, context):
        for obj in context.selected_objects:
            obj["bento_surface_excluded"] = self.excluded
        return {"FINISHED"}


class Bento_OT_exclude_and_hide(Operator):
    bl_idname = "bento.exclude_and_hide_surfaces"
    bl_label = "排除并隐藏选中对象"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        selected = list(context.selected_objects)
        if not selected:
            self.report({"WARNING"}, "没有选中对象")
            return {"CANCELLED"}
        if context.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        for obj in selected:
            obj["bento_surface_excluded"] = True
            obj.hide_set(True)
        self.report({"INFO"}, f"已排除并隐藏 {len(selected)} 个对象")
        return {"FINISHED"}


class Bento_OT_classify(Operator):
    bl_idname = "bento.classify_building_surfaces"
    bl_label = "计算内外分类"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        volumes = iter_volumes(context)
        if not volumes:
            self.report({"ERROR"}, "请先创建或指定至少一个室内方块")
            return {"CANCELLED"}
        targets = iter_target_objects(context)
        if not targets:
            self.report({"ERROR"}, "没有可处理的网格对象")
            return {"CANCELLED"}

        settings = context.scene.bento_surface_splitter
        target_bvh, _ = build_target_bvh(context, targets)
        search_distance = calculate_adaptive_search_distance(
            targets,
            volumes,
            max(settings.surface_probe_distance, 0.0001),
        )
        volume_samples = [
            point
            for volume in volumes
            for point in volume_sample_points(volume)
        ]
        interior = exterior = 0
        for obj in targets:
            if context.mode != "OBJECT":
                bpy.ops.object.mode_set(mode="OBJECT")
            attribute = ensure_attribute(obj.data)
            class_values = []
            for polygon in obj.data.polygons:
                value = classify_polygon(
                    obj,
                    polygon,
                    volumes,
                    volume_samples,
                    settings,
                    target_bvh,
                    search_distance,
                )
                class_values.append(value)
            propagate_interior_to_adjacent_faces(obj, class_values, settings)
            propagate_interior_to_coplanar_faces(obj, class_values, volumes, settings)
            for polygon, value in zip(obj.data.polygons, class_values):
                attribute.data[polygon.index].value = value
                if value == CLASS_INTERIOR:
                    interior += 1
                else:
                    exterior += 1
            obj.data.update()
        self.report({"INFO"}, f"分类完成：室内 {interior} 面，外墙 {exterior} 面")
        return {"FINISHED"}


class Bento_OT_select_class(Operator):
    bl_idname = "bento.select_surface_class"
    bl_label = "选择分类面"
    bl_options = {"REGISTER", "UNDO"}

    class_value: EnumProperty(
        items=[("0", "未分类", ""), ("1", "室内", ""), ("2", "外墙", ""), ("3", "排除", "")],
        default="1",
    )

    def execute(self, context):
        try:
            select_class_faces(context, int(self.class_value))
        except RuntimeError as exception:
            self.report({"ERROR"}, str(exception))
            return {"CANCELLED"}
        return {"FINISHED"}


class Bento_OT_assign_class(Operator):
    bl_idname = "bento.assign_surface_class"
    bl_label = "设置选中面的分类"
    bl_options = {"REGISTER", "UNDO"}

    class_value: EnumProperty(
        items=[("0", "未分类", ""), ("1", "室内", ""), ("2", "外墙", ""), ("3", "排除", "")],
        default="1",
    )

    def execute(self, context):
        if context.mode != "EDIT_MESH":
            self.report({"ERROR"}, "请先进入编辑模式并选择面")
            return {"CANCELLED"}
        count = assign_selected_faces(context, int(self.class_value))
        self.report({"INFO"}, f"已修改 {count} 个面")
        return {"FINISHED"}


class Bento_OT_overwrite_class(Operator):
    bl_idname = "bento.overwrite_surface_class"
    bl_label = "用当前选择覆盖分类"
    bl_options = {"REGISTER", "UNDO"}

    class_value: EnumProperty(
        items=[("1", "室内", ""), ("2", "外墙", "")],
        default="1",
    )

    def execute(self, context):
        if context.mode != "EDIT_MESH":
            self.report({"ERROR"}, "请先进入编辑模式并选择面")
            return {"CANCELLED"}
        class_value = int(self.class_value)
        fallback_value = CLASS_EXTERIOR if class_value == CLASS_INTERIOR else CLASS_INTERIOR
        count = overwrite_class_from_selection(context, class_value, fallback_value)
        label = "室内" if class_value == CLASS_INTERIOR else "外墙"
        self.report({"INFO"}, f"已用当前选择覆盖{label}分类，修改 {count} 个面")
        return {"FINISHED"}


class Bento_OT_split_surfaces(Operator):
    bl_idname = "bento.split_building_surfaces"
    bl_label = "确认并分离"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        if context.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        settings = context.scene.bento_surface_splitter
        targets = iter_target_objects(context)
        if not targets:
            self.report({"ERROR"}, "没有可分离的对象")
            return {"CANCELLED"}

        root_collection = bpy.data.collections.get("Bento_BuildingSplit")
        if root_collection is None:
            root_collection = bpy.data.collections.new("Bento_BuildingSplit")
            context.scene.collection.children.link(root_collection)

        interior_parent = ensure_parent_empty(context, settings.interior_name)
        exterior_parent = ensure_parent_empty(context, settings.exterior_name)
        roof_parent = ensure_parent_empty(context, settings.roof_name)
        collections = {
            CLASS_INTERIOR: get_or_create_child_collection(root_collection, settings.interior_name),
            CLASS_EXTERIOR: get_or_create_child_collection(root_collection, settings.exterior_name),
        }
        for obj in context.scene.objects:
            if not is_excluded_object(obj) or is_volume_object(obj) or obj in {interior_parent, exterior_parent, roof_parent}:
                continue
            obj.parent = roof_parent

        created = 0
        for source in targets:
            for class_value, suffix, parent in (
                (CLASS_INTERIOR, settings.interior_name, interior_parent),
                (CLASS_EXTERIOR, settings.exterior_name, exterior_parent),
            ):
                output = copy_polygon_subset(source, class_value, f"{source.name}_{suffix}")
                if output is None:
                    continue
                collections[class_value].objects.link(output)
                output.parent = parent
                created += 1
        if settings.export_after_split and settings.export_path:
            bpy.ops.bento.export_building_fbx()
        self.report({"INFO"}, f"已生成 {created} 个分类对象；原始对象未覆盖")
        return {"FINISHED"}


class Bento_OT_export_fbx(Operator):
    bl_idname = "bento.export_building_fbx"
    bl_label = "导出分类 FBX"

    def execute(self, context):
        settings = context.scene.bento_surface_splitter
        path = bpy.path.abspath(settings.export_path)
        if not path:
            self.report({"ERROR"}, "请设置 FBX 输出路径")
            return {"CANCELLED"}
        if not path.lower().endswith(".fbx"):
            path += ".fbx"
        outputs = [obj for obj in context.scene.objects if is_output_object(obj)]
        if not outputs:
            self.report({"ERROR"}, "没有已分离的输出对象")
            return {"CANCELLED"}
        bpy.ops.object.select_all(action="DESELECT")
        for obj in outputs:
            obj.select_set(True)
        context.view_layer.objects.active = outputs[0]
        bpy.ops.export_scene.fbx(
            filepath=path,
            use_selection=True,
            object_types={"MESH", "EMPTY"},
            use_mesh_modifiers=True,
            mesh_smooth_type="FACE",
            add_leaf_bones=False,
            bake_anim=False,
            path_mode="AUTO",
        )
        self.report({"INFO"}, f"已导出：{path}")
        return {"FINISHED"}


class Bento_PT_surface_splitter(Panel):
    bl_label = "建筑内外墙拆分"
    bl_idname = "Bento_PT_surface_splitter"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Bento"

    def draw(self, context):
        layout = self.layout
        settings = context.scene.bento_surface_splitter
        box = layout.box()
        box.label(text="1. 室内体积")
        row = box.row(align=True)
        row.operator("bento.create_interior_volume", text="创建方块")
        row.operator("bento.mark_interior_volume", text="指定选中")
        box.label(text=f"当前室内方块：{len(iter_volumes(context))}")

        box = layout.box()
        box.label(text="2. 处理范围与排除")
        box.prop(settings, "target_scope")
        targets = iter_target_objects(context)
        box.label(text=f"当前待处理网格：{len(targets)}")
        if settings.target_scope == "SELECTED":
            box.label(text="提示：创建方块会改变当前选择，建议使用“Unity 导入模型”", icon="INFO")
        row = box.row(align=True)
        op = row.operator("bento.toggle_surface_excluded", text="排除选中对象")
        op.excluded = True
        op = row.operator("bento.toggle_surface_excluded", text="取消排除")
        op.excluded = False
        box.operator("bento.exclude_and_hide_surfaces", text="排除并隐藏选中对象", icon="HIDE_ON")

        box = layout.box()
        box.label(text="3. 自动分类")
        box.prop(settings, "upward_normal_threshold")
        box.prop(settings, "surface_probe_distance")
        box.prop(settings, "volume_tolerance")
        box.operator("bento.classify_building_surfaces", icon="MOD_BUILD")

        box = layout.box()
        box.label(text="4. 查看分类结果")
        row = box.row(align=True)
        op = row.operator("bento.select_surface_class", text="选择室内面")
        op.class_value = "1"
        op = row.operator("bento.select_surface_class", text="选择外墙面")
        op.class_value = "2"
        box.label(text="这里只读取面上的分类标记", icon="INFO")

        box = layout.box()
        box.label(text="5. 手动修补")
        box.label(text="先增减面选择，再保存当前选中面", icon="INFO")
        row = box.row(align=True)
        op = row.operator("bento.assign_surface_class", text="保留选中面为室内")
        op.class_value = "1"
        op = row.operator("bento.assign_surface_class", text="保留选中面为外墙")
        op.class_value = "2"
        row = box.row(align=True)
        op = row.operator("bento.overwrite_surface_class", text="覆盖保存为室内")
        op.class_value = "1"
        op = row.operator("bento.overwrite_surface_class", text="覆盖保存为外墙")
        op.class_value = "2"

        box = layout.box()
        box.label(text="6. 生成分类对象与导出")
        box.prop(settings, "interior_name")
        box.prop(settings, "exterior_name")
        box.prop(settings, "roof_name")
        box.operator("bento.split_building_surfaces", icon="MOD_EXPLODE")
        box.prop(settings, "export_path")
        box.prop(settings, "export_after_split")
        box.operator("bento.export_building_fbx", icon="EXPORT")


CLASSES = (
    BentoSurfaceSplitterSettings,
    Bento_OT_create_interior_volume,
    Bento_OT_mark_volume,
    Bento_OT_toggle_excluded,
    Bento_OT_exclude_and_hide,
    Bento_OT_classify,
    Bento_OT_select_class,
    Bento_OT_assign_class,
    Bento_OT_overwrite_class,
    Bento_OT_split_surfaces,
    Bento_OT_export_fbx,
    Bento_PT_surface_splitter,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.bento_surface_splitter = PointerProperty(type=BentoSurfaceSplitterSettings)


def unregister():
    del bpy.types.Scene.bento_surface_splitter
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
