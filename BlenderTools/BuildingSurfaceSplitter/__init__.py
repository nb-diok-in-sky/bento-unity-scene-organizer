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
import os
from math import cos, radians
from bpy.props import BoolProperty, EnumProperty, FloatProperty, PointerProperty, StringProperty
from bpy.types import Operator, Panel, PropertyGroup
from mathutils import Vector
from mathutils.bvhtree import BVHTree


CLASS_ATTRIBUTE = "bento_surface_class"
RULE_ATTRIBUTE = "bento_surface_rule"
CUT_FACE_ATTRIBUTE = "bento_horizontal_cut_face"
CUT_PREVIEW_NAME = "Bento_HorizontalCutPreview"
CLASS_UNCLASSIFIED = 0
CLASS_INTERIOR = 1
CLASS_EXTERIOR = 2
CLASS_EXCLUDED = 3
RULE_BASE = 1
RULE_ADJACENT = 2
RULE_COPLANAR = 3
RULE_VOLUME_EDGE = 4
RULE_FLOOR_LOW = 5
RULE_HORIZONTAL = 6
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


def ensure_rule_attribute(mesh):
    attribute = mesh.attributes.get(RULE_ATTRIBUTE)
    if attribute and (attribute.domain != "FACE" or attribute.data_type != "INT"):
        mesh.attributes.remove(attribute)
        attribute = None
    return attribute or mesh.attributes.new(RULE_ATTRIBUTE, "INT", "FACE")


def ensure_cut_face_attribute(mesh):
    attribute = mesh.attributes.get(CUT_FACE_ATTRIBUTE)
    if attribute and (attribute.domain != "FACE" or attribute.data_type != "INT"):
        mesh.attributes.remove(attribute)
        attribute = None
    return attribute or mesh.attributes.new(CUT_FACE_ATTRIBUTE, "INT", "FACE")


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


def point_inside_volume_vertical_span(world_point, volume_obj, tolerance):
    bounds_min, bounds_max = volume_world_bounds(volume_obj)
    return bounds_min.z - tolerance <= world_point.z <= bounds_max.z + tolerance


def point_inside_volume_expanded(world_point, volume_obj, horizontal_margin, vertical_margin):
    bounds_min, bounds_max = volume_world_bounds(volume_obj)
    return (
        bounds_min.x - horizontal_margin <= world_point.x <= bounds_max.x + horizontal_margin
        and bounds_min.y - horizontal_margin <= world_point.y <= bounds_max.y + horizontal_margin
        and bounds_min.z - vertical_margin <= world_point.z <= bounds_max.z + vertical_margin
    )


def volume_world_bounds(volume_obj):
    corners = [volume_obj.matrix_world @ Vector(corner) for corner in volume_obj.bound_box]
    bounds_min = Vector(tuple(min(point[axis] for point in corners) for axis in range(3)))
    bounds_max = Vector(tuple(max(point[axis] for point in corners) for axis in range(3)))
    return bounds_min, bounds_max


def build_interior_footprints(targets, volumes, settings):
    footprints = []
    tolerance = max(settings.volume_tolerance, 0.0005)
    volume_bounds = [volume_world_bounds(volume) for volume in volumes]
    for obj in targets:
        if not is_floor_object(obj):
            continue
        normal_matrix = obj.matrix_world.to_3x3().inverted_safe().transposed()
        for polygon in obj.data.polygons:
            normal = (normal_matrix @ polygon.normal).normalized()
            if normal.z < settings.upward_normal_threshold:
                continue
            vertices = [
                obj.matrix_world @ obj.data.vertices[index].co
                for index in polygon.vertices
            ]
            if len(vertices) < 3:
                continue
            min_x = min(vertex.x for vertex in vertices)
            max_x = max(vertex.x for vertex in vertices)
            min_y = min(vertex.y for vertex in vertices)
            max_y = max(vertex.y for vertex in vertices)
            floor_z = max(vertex.z for vertex in vertices)
            matching_heights = [
                bounds_max.z
                for bounds_min, bounds_max in volume_bounds
                if max_x >= bounds_min.x - tolerance
                and min_x <= bounds_max.x + tolerance
                and max_y >= bounds_min.y - tolerance
                and min_y <= bounds_max.y + tolerance
            ]
            ceiling_z = max(matching_heights, default=floor_z + 10.0)
            footprints.append(
                (min_x, max_x, min_y, max_y, floor_z, ceiling_z, vertices)
            )
    return footprints


def floor_boundary_planes(footprints, expansion=0.0):
    planes = []
    for footprint in footprints:
        min_x, max_x, min_y, max_y = footprint[:4]
        planes.extend((
            ("X", min_x - expansion),
            ("X", max_x + expansion),
            ("Y", min_y - expansion),
            ("Y", max_y + expansion),
        ))
    unique = []
    for axis, value in planes:
        if not any(existing_axis == axis and abs(existing_value - value) <= 1e-5
                   for existing_axis, existing_value in unique):
            unique.append((axis, value))
    return unique


def polygon_crosses_plane(obj, polygon, axis, value, epsilon):
    axis_index = 0 if axis == "X" else 1
    coordinates = [
        (obj.matrix_world @ obj.data.vertices[index].co)[axis_index]
        for index in polygon.vertices
    ]
    return min(coordinates) < value - epsilon and max(coordinates) > value + epsilon


def polygon_crosses_segment_xy(obj, polygon, first, second):
    vertices = [
        obj.matrix_world @ obj.data.vertices[index].co
        for index in polygon.vertices
    ]
    if not vertices:
        return False
    face_min_x = min(vertex.x for vertex in vertices)
    face_max_x = max(vertex.x for vertex in vertices)
    face_min_y = min(vertex.y for vertex in vertices)
    face_max_y = max(vertex.y for vertex in vertices)
    segment_min_x = min(first.x, second.x)
    segment_max_x = max(first.x, second.x)
    segment_min_y = min(first.y, second.y)
    segment_max_y = max(first.y, second.y)
    return (
        face_max_x >= segment_min_x
        and face_min_x <= segment_max_x
        and face_max_y >= segment_min_y
        and face_min_y <= segment_max_y
    )


def is_horizontal_cut_candidate(obj, polygon, footprints, settings, top_z):
    if is_floor_object(obj):
        return False
    normal_matrix = obj.matrix_world.to_3x3().inverted_safe().transposed()
    normal = (normal_matrix @ polygon.normal).normalized()
    horizontal_threshold = cos(settings.horizontal_cut_angle)
    if abs(normal.z) < horizontal_threshold:
        return False
    center = obj.matrix_world @ polygon.center
    if center.z >= top_z - settings.horizontal_cut_top_exclusion:
        return False
    epsilon = max(settings.volume_tolerance * 4.0, 0.0005)
    return any(
        polygon_crosses_plane(obj, polygon, axis, value, epsilon)
        for axis, value in floor_boundary_planes(
            footprints, settings.horizontal_cut_expansion
        )
    )


def face_touches_floor_footprint(points, footprint, horizontal_margin, vertical_margin):
    min_x, max_x, min_y, max_y, floor_z, ceiling_z = footprint[:6]
    face_min = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    face_max = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    return (
        face_max.x >= min_x - horizontal_margin
        and face_min.x <= max_x + horizontal_margin
        and face_max.y >= min_y - horizontal_margin
        and face_min.y <= max_y + horizontal_margin
        and face_max.z >= floor_z - vertical_margin
        and face_min.z <= ceiling_z + vertical_margin
    )


def point_inside_floor_footprint(point, footprint, horizontal_margin, vertical_margin):
    min_x, max_x, min_y, max_y, floor_z, ceiling_z = footprint[:6]
    return (
        min_x - horizontal_margin <= point.x <= max_x + horizontal_margin
        and min_y - horizontal_margin <= point.y <= max_y + horizontal_margin
        and floor_z - vertical_margin <= point.z <= ceiling_z + vertical_margin
    )


def point_inside_floor_polygon(point, footprint, margin=0.0):
    vertices = footprint[6]
    if len(vertices) < 3:
        return False
    inside = False
    x, y = point.x, point.y
    previous = vertices[-1]
    for current in vertices:
        if ((current.y > y) != (previous.y > y)):
            crossing_x = (
                (previous.x - current.x) * (y - current.y)
                / max(previous.y - current.y, 1e-12)
                + current.x
            )
            if x < crossing_x:
                inside = not inside
        previous = current
    if inside:
        return True
    return any((Vector((vertex.x, vertex.y)) - Vector((x, y))).length <= margin for vertex in vertices)


def offset_floor_polygon(vertices, expansion):
    if not vertices:
        return []
    center = sum(vertices, Vector()) / len(vertices)
    result = []
    for vertex in vertices:
        direction = Vector((vertex.x - center.x, vertex.y - center.y, 0.0))
        if direction.length_squared > 1e-12:
            direction.normalize()
        result.append(Vector((vertex.x, vertex.y, vertex.z)) + direction * expansion)
    return result


def extract_floor_boundary_segments(targets, settings):
    segments = []
    threshold = cos(settings.horizontal_cut_angle)
    for obj in targets:
        if not is_floor_object(obj):
            continue
        normal_matrix = obj.matrix_world.to_3x3().inverted_safe().transposed()
        edge_use = {}
        for polygon in obj.data.polygons:
            normal = (normal_matrix @ polygon.normal).normalized()
            if normal.z < threshold:
                continue
            for edge_key in polygon.edge_keys:
                edge_use[edge_key] = edge_use.get(edge_key, 0) + 1
        for edge_key, use_count in edge_use.items():
            if use_count != 1:
                continue
            first, second = (
                obj.matrix_world @ obj.data.vertices[index].co
                for index in edge_key
            )
            segments.append((first, second))
    return segments


def expand_boundary_segment(first, second, center, expansion):
    midpoint = (first + second) * 0.5
    outward = Vector((midpoint.x - center.x, midpoint.y - center.y, 0.0))
    if outward.length_squared > 1e-12:
        outward.normalize()
    return first + outward * expansion, second + outward * expansion


def remove_cut_preview():
    preview = bpy.data.objects.get(CUT_PREVIEW_NAME)
    if preview is None:
        return
    data = preview.data
    bpy.data.objects.remove(preview, do_unlink=True)
    if data and data.users == 0:
        bpy.data.curves.remove(data)


def iter_saved_cut_faces(targets):
    for obj in targets:
        attribute = obj.data.attributes.get(CUT_FACE_ATTRIBUTE)
        if attribute is None or len(attribute.data) != len(obj.data.polygons):
            continue
        for polygon in obj.data.polygons:
            if attribute.data[polygon.index].value:
                yield obj, polygon


def create_cut_preview(context, footprints, targets, settings):
    remove_cut_preview()
    curve = bpy.data.curves.new(CUT_PREVIEW_NAME, type="CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = 0.012
    curve.bevel_resolution = 0
    curve.resolution_u = 1
    candidate_levels = {}
    for obj, polygon in iter_saved_cut_faces(targets):
        center = obj.matrix_world @ polygon.center
        candidate_levels.setdefault(round(center.z, 5), []).append((obj, polygon))

    boundary_segments = extract_floor_boundary_segments(targets, settings)
    all_boundary_points = [
        point for segment in boundary_segments for point in segment
    ]
    center = (
        sum(all_boundary_points, Vector()) / len(all_boundary_points)
        if all_boundary_points else Vector()
    )
    preview_count = 0
    for level, candidates in candidate_levels.items():
        for first, second in boundary_segments:
            expanded_first, expanded_second = expand_boundary_segment(
                first, second, center, settings.horizontal_cut_expansion
            )
            if not any(
                polygon_crosses_segment_xy(obj, polygon, expanded_first, expanded_second)
                for obj, polygon in candidates
            ):
                continue
            spline = curve.splines.new("POLY")
            spline.points.add(1)
            spline.points[0].co = (
                expanded_first.x, expanded_first.y, level, 1.0
            )
            spline.points[1].co = (
                expanded_second.x, expanded_second.y, level, 1.0
            )
            preview_count += 1
    preview = bpy.data.objects.new(CUT_PREVIEW_NAME, curve)
    context.scene.collection.objects.link(preview)
    preview["bento_cut_preview"] = True
    preview.display_type = "WIRE"
    preview.color = (1.0, 0.05, 0.05, 1.0)
    preview.show_in_front = True
    return preview, preview_count


def normal_faces_footprint_interior(center, normal, footprint):
    min_x, max_x, min_y, max_y, _floor_z, _ceiling_z = footprint[:6]
    footprint_center = Vector(((min_x + max_x) * 0.5, (min_y + max_y) * 0.5, center.z))
    direction = footprint_center - center
    direction.z = 0.0
    if direction.length_squared <= 1e-12:
        return True
    direction.normalize()
    horizontal_normal = Vector((normal.x, normal.y, 0.0))
    if horizontal_normal.length_squared <= 1e-12:
        return False
    horizontal_normal.normalize()
    return horizontal_normal.dot(direction) >= 0.15


def is_outer_parallel_frame_candidate(
    obj, polygon, normal, footprints, settings
):
    if abs(normal.z) >= settings.upward_normal_threshold:
        return False
    center = obj.matrix_world @ polygon.center
    points = polygon_sample_points(obj, polygon)
    tolerance = max(settings.volume_tolerance, 0.0005)
    for footprint in footprints:
        if not face_touches_floor_footprint(
            points, footprint, 0.3, tolerance * 8.0
        ):
            continue
        min_x, max_x, min_y, max_y = footprint[:4]
        distances = {
            "MIN_X": abs(center.x - min_x),
            "MAX_X": abs(center.x - max_x),
            "MIN_Y": abs(center.y - min_y),
            "MAX_Y": abs(center.y - max_y),
        }
        boundary = min(distances, key=distances.get)
        if distances[boundary] > 0.35:
            continue
        wall_axis = Vector((1.0, 0.0, 0.0)) if boundary.endswith("X") else Vector((0.0, 1.0, 0.0))
        horizontal_normal = Vector((normal.x, normal.y, 0.0))
        if horizontal_normal.length_squared <= 1e-12:
            continue
        horizontal_normal.normalize()
        if abs(horizontal_normal.dot(wall_axis)) > 0.35:
            continue
        outward = {
            "MIN_X": Vector((-1.0, 0.0, 0.0)),
            "MAX_X": Vector((1.0, 0.0, 0.0)),
            "MIN_Y": Vector((0.0, -1.0, 0.0)),
            "MAX_Y": Vector((0.0, 1.0, 0.0)),
        }[boundary]
        outside_point = center + outward * 0.2
        inside_point = center - outward * 0.2
        outside_in_floor = point_inside_floor_polygon(outside_point, footprint, tolerance)
        inside_in_floor = point_inside_floor_polygon(inside_point, footprint, tolerance)
        if inside_in_floor and not outside_in_floor:
            return True
    return False


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


def propagate_interior_to_adjacent_faces(obj, class_values, rule_values, settings):
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
                rule_values[index] = RULE_ADJACENT
                changed = True


def polygon_world_geometry(obj, polygon, normal_matrix):
    vertices = [obj.matrix_world @ obj.data.vertices[index].co for index in polygon.vertices]
    center = obj.matrix_world @ polygon.center
    normal = (normal_matrix @ polygon.normal).normalized()
    radius = max(((vertex - center).length for vertex in vertices), default=0.0)
    min_z = min((vertex.z for vertex in vertices), default=center.z)
    max_z = max((vertex.z for vertex in vertices), default=center.z)
    return center, normal, radius, min_z, max_z


def propagate_interior_to_coplanar_faces(obj, class_values, rule_values, volumes, settings):
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
                rule_values[index] = RULE_COPLANAR
                break


def classify_faces_near_interior_volume(
    obj, class_values, rule_values, volumes, footprints, settings
):
    if not volumes and not footprints:
        return
    tolerance = max(settings.volume_tolerance, 0.0005)
    normal_matrix = obj.matrix_world.to_3x3().inverted_safe().transposed()
    for polygon in obj.data.polygons:
        index = polygon.index
        if class_values[index] != CLASS_EXTERIOR:
            continue
        normal = (normal_matrix @ polygon.normal).normalized()
        if abs(normal.z) >= settings.upward_normal_threshold:
            continue
        points = polygon_sample_points(obj, polygon)
        radius = max(
            ((point - (obj.matrix_world @ polygon.center)).length for point in points),
            default=0.0,
        )
        horizontal_margin = max(tolerance * 8.0, min(radius * 0.2, 0.25))
        if any(
            face_touches_interior_footprint(points, volume, horizontal_margin, tolerance * 4.0)
            for volume in volumes
        ):
            class_values[index] = CLASS_INTERIOR
            rule_values[index] = RULE_VOLUME_EDGE


def supplement_low_faces_from_floor_footprints(
    obj, class_values, rule_values, footprints, settings
):
    if not footprints:
        return
    tolerance = max(settings.volume_tolerance, 0.0005)
    normal_matrix = obj.matrix_world.to_3x3().inverted_safe().transposed()
    geometry = [
        polygon_world_geometry(obj, polygon, normal_matrix)
        for polygon in obj.data.polygons
    ]
    seeds = [
        index
        for index, value in enumerate(class_values)
        if value == CLASS_INTERIOR
        and abs(geometry[index][1].z) < settings.upward_normal_threshold
    ]
    for polygon in obj.data.polygons:
        index = polygon.index
        if class_values[index] != CLASS_EXTERIOR:
            continue
        center, normal, radius, min_z, max_z = geometry[index]
        if abs(normal.z) >= settings.upward_normal_threshold:
            continue
        matching_footprints = [
            footprint
            for footprint in footprints
            if face_touches_floor_footprint(
                polygon_sample_points(obj, polygon),
                footprint,
                max(tolerance * 8.0, min(radius * 0.08, 0.08)),
                tolerance * 8.0,
            )
            and max_z <= footprint[4] + max(0.6, radius * 1.5)
            and normal_faces_footprint_interior(center, normal, footprint)
        ]
        if not matching_footprints:
            continue
        for seed_index in seeds:
            seed_center, seed_normal, seed_radius, seed_min_z, seed_max_z = geometry[seed_index]
            if seed_normal.dot(normal) < 0.94:
                continue
            plane_distance = abs((center - seed_center).dot(seed_normal))
            if plane_distance > max(0.12, tolerance * 12.0):
                continue
            horizontal_delta = center - seed_center
            horizontal_delta.z = 0.0
            if horizontal_delta.length <= radius + seed_radius + 0.35:
                class_values[index] = CLASS_INTERIOR
                rule_values[index] = RULE_FLOOR_LOW
                break


def supplement_inward_floor_boundary_faces(
    obj, class_values, rule_values, footprints, settings
):
    if not footprints:
        return
    normal_matrix = obj.matrix_world.to_3x3().inverted_safe().transposed()
    tolerance = max(settings.volume_tolerance, 0.0005)
    for polygon in obj.data.polygons:
        index = polygon.index
        if class_values[index] != CLASS_EXTERIOR:
            continue
        normal = (normal_matrix @ polygon.normal).normalized()
        if abs(normal.z) >= settings.upward_normal_threshold:
            continue
        center = obj.matrix_world @ polygon.center
        points = polygon_sample_points(obj, polygon)
        if any(
            face_touches_floor_footprint(
                points, footprint, tolerance * 8.0, tolerance * 8.0
            )
            and normal_faces_footprint_interior(center, normal, footprint)
            and min(
                abs(center.x - footprint[0]),
                abs(center.x - footprint[1]),
                abs(center.y - footprint[2]),
                abs(center.y - footprint[3]),
            ) <= 0.18
            for footprint in footprints
        ):
            class_values[index] = CLASS_INTERIOR
            rule_values[index] = RULE_FLOOR_LOW


def face_touches_interior_footprint(points, volume, horizontal_margin, vertical_margin):
    bounds_min, bounds_max = volume_world_bounds(volume)
    face_min = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    face_max = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    return (
        face_max.x >= bounds_min.x - horizontal_margin
        and face_min.x <= bounds_max.x + horizontal_margin
        and face_max.y >= bounds_min.y - horizontal_margin
        and face_min.y <= bounds_max.y + horizontal_margin
        and face_max.z >= bounds_min.z - vertical_margin
        and face_min.z <= bounds_max.z + vertical_margin
    )


def classify_horizontal_faces_by_interior_footprint(
    obj, class_values, rule_values, volumes, footprints, settings
):
    if not volumes and not footprints:
        return
    tolerance = max(settings.volume_tolerance, 0.0005)
    normal_matrix = obj.matrix_world.to_3x3().inverted_safe().transposed()
    for polygon in obj.data.polygons:
        index = polygon.index
        if class_values[index] != CLASS_EXTERIOR:
            continue
        normal = (normal_matrix @ polygon.normal).normalized()
        if normal.z < settings.upward_normal_threshold:
            continue
        points = polygon_sample_points(obj, polygon)
        inside_count = sum(
            1
            for point in points
            if any(
                point_inside_volume_horizontal(point, volume, tolerance * 4.0)
                and point_inside_volume_vertical_span(point, volume, tolerance * 8.0)
                for volume in volumes
            ) or any(
                footprint[4] - tolerance * 8.0
                <= point.z
                <= footprint[5] + tolerance * 8.0
                and point_inside_floor_polygon(point, footprint, tolerance * 4.0)
                for footprint in footprints
            )
        )
        if inside_count >= max(1, (len(points) + 2) // 3):
            class_values[index] = CLASS_INTERIOR
            rule_values[index] = RULE_HORIZONTAL


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


def is_visible_from_volume(
    surface_points, volume_samples, target_bvh, settings, search_distance,
    surface_normal=None, facing_mode="ANY"
):
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
            if surface_normal is not None and facing_mode != "ANY":
                faces_origin = surface_normal.dot(-direction) > 0.0
                if facing_mode == "TOWARD_ORIGIN" and not faces_origin:
                    continue
                if facing_mode == "AWAY_FROM_ORIGIN" and faces_origin:
                    continue
            hit = target_bvh.ray_cast(origin, direction, distance + surface_epsilon)
            hit_location = hit[0]
            if hit_location is not None and (hit_location - surface_point).length <= surface_epsilon * 4.0:
                return True
    return False


def is_visible_from_volume_with_facing(
    surface_points, surface_normal, volume_samples, target_bvh, settings, search_distance,
    require_facing
):
    if target_bvh is None:
        return False
    surface_epsilon = max(settings.volume_tolerance, 0.0005)
    for surface_point in surface_points:
        for origin in volume_samples:
            direction = surface_point - origin
            distance = direction.length
            if distance <= surface_epsilon or distance > search_distance:
                continue
            direction.normalize()
            hit_location = target_bvh.ray_cast(
                origin, direction, distance + surface_epsilon
            )[0]
            if hit_location is None or (hit_location - surface_point).length > surface_epsilon * 4.0:
                continue
            faces_origin = surface_normal.dot(origin - surface_point) > 0.0
            if faces_origin == require_facing:
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


def compose_output_name(source_name, category_name, placement, use_separator):
    separator = "_" if use_separator and source_name and category_name else ""
    if placement == "PREFIX":
        return f"{category_name}{separator}{source_name}"
    return f"{source_name}{separator}{category_name}"


def remove_previous_outputs():
    outputs = [obj for obj in bpy.data.objects if is_output_object(obj)]
    for obj in outputs:
        data = obj.data if is_mesh_object(obj) else None
        bpy.data.objects.remove(obj, do_unlink=True)
        if data is not None and data.users == 0:
            bpy.data.meshes.remove(data)
    for collection in list(bpy.data.collections):
        if collection.get("bento_surface_output_collection") and not collection.objects and not collection.children:
            bpy.data.collections.remove(collection)


def resolve_export_path(settings):
    source_path = bpy.path.abspath(settings.source_fbx_path)
    if settings.overwrite_source_fbx and source_path:
        return source_path
    configured = bpy.path.abspath(settings.export_path)
    if configured:
        return configured if configured.lower().endswith(".fbx") else configured + ".fbx"
    if source_path:
        directory = os.path.dirname(source_path)
        stem = settings.output_fbx_name.strip() or (
            os.path.splitext(os.path.basename(source_path))[0] + "_processed"
        )
        return os.path.join(directory, stem + ".fbx")
    return ""


def count_rule_hits(context):
    counts = {value: 0 for value in range(RULE_BASE, RULE_HORIZONTAL + 1)}
    for obj in iter_target_objects(context):
        attribute = obj.data.attributes.get(RULE_ATTRIBUTE)
        if attribute is None or len(attribute.data) != len(obj.data.polygons):
            continue
        for item in attribute.data:
            if item.value in counts:
                counts[item.value] += 1
    return counts


def evaluate_debug_rule(
    rule_value, obj, polygon, volumes, volume_samples, footprints,
    settings, target_bvh, search_distance
):
    center = obj.matrix_world @ polygon.center
    normal_matrix = obj.matrix_world.to_3x3().inverted_safe().transposed()
    normal = (normal_matrix @ polygon.normal).normalized()
    points = polygon_sample_points(obj, polygon)
    tolerance = max(settings.volume_tolerance, 0.0005)

    if rule_value == "UPWARD":
        return normal.z >= settings.upward_normal_threshold
    if rule_value == "FLOOR_OBJECT":
        return is_floor_object(obj) and normal.z >= settings.upward_normal_threshold
    if rule_value == "CENTER_IN_VOLUME":
        return any(
            point_inside_volume(center, volume, settings.volume_tolerance)
            for volume in volumes
        )
    if rule_value == "RAY_VISIBLE":
        return (
            abs(normal.z) < settings.upward_normal_threshold
            and is_visible_from_volume(
                points, volume_samples, target_bvh, settings, search_distance
            )
        )
    if rule_value == "RAY_TOWARD_ORIGIN":
        return (
            abs(normal.z) < settings.upward_normal_threshold
            and is_visible_from_volume(
                points,
                volume_samples,
                target_bvh,
                settings,
                search_distance,
                normal,
                "TOWARD_ORIGIN",
            )
        )
    if rule_value == "RAY_AWAY_FROM_ORIGIN":
        return (
            abs(normal.z) < settings.upward_normal_threshold
            and is_visible_from_volume(
                points,
                volume_samples,
                target_bvh,
                settings,
                search_distance,
                normal,
                "AWAY_FROM_ORIGIN",
            )
        )
    if rule_value == "OUTER_FRAME":
        return is_outer_parallel_frame_candidate(
            obj, polygon, normal, footprints, settings
        )
    if rule_value == "RAY_MINUS_OUTER_FRAME":
        return (
            abs(normal.z) < settings.upward_normal_threshold
            and is_visible_from_volume(
                points,
                volume_samples,
                target_bvh,
                settings,
                search_distance,
                normal,
                "TOWARD_ORIGIN",
            )
            and not is_outer_parallel_frame_candidate(
                obj, polygon, normal, footprints, settings
            )
        )
    if rule_value == "VOLUME_EDGE":
        if abs(normal.z) >= settings.upward_normal_threshold:
            return False
        radius = max(((point - center).length for point in points), default=0.0)
        margin = max(tolerance * 8.0, min(radius * 0.2, 0.25))
        return any(
            face_touches_interior_footprint(points, volume, margin, tolerance * 4.0)
            for volume in volumes
        )
    if rule_value == "FLOOR_FOOTPRINT_SIDE":
        if abs(normal.z) >= settings.upward_normal_threshold:
            return False
        return any(
            face_touches_floor_footprint(points, footprint, tolerance * 8.0, tolerance * 8.0)
            and normal_faces_footprint_interior(center, normal, footprint)
            and min(
                abs(center.x - footprint[0]),
                abs(center.x - footprint[1]),
                abs(center.y - footprint[2]),
                abs(center.y - footprint[3]),
            ) <= 0.18
            for footprint in footprints
        )
    if rule_value == "HORIZONTAL_FOOTPRINT":
        if normal.z < settings.upward_normal_threshold:
            return False
        inside_count = sum(
            1
            for point in points
            if any(
                point_inside_volume_horizontal(point, volume, tolerance * 4.0)
                and point_inside_volume_vertical_span(point, volume, tolerance * 8.0)
                for volume in volumes
            )
            or any(
                footprint[4] - tolerance * 8.0
                <= point.z
                <= footprint[5] + tolerance * 8.0
                and point_inside_floor_polygon(point, footprint, tolerance * 4.0)
                for footprint in footprints
            )
        )
        return inside_count >= max(1, (len(points) + 2) // 3)
    return False


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
    name_placement: EnumProperty(
        name="分类名称位置",
        items=[
            ("SUFFIX", "后缀", "原名_分类名"),
            ("PREFIX", "前缀", "分类名_原名"),
        ],
        default="SUFFIX",
    )
    use_name_separator: BoolProperty(name="名称加入下划线", default=True)
    replace_source_objects: BoolProperty(
        name="分离后删除原对象",
        description="成功生成分类对象后删除分离前的源网格对象",
        default=True,
    )
    source_fbx_path: StringProperty(name="源 FBX", subtype="FILE_PATH")
    output_fbx_name: StringProperty(name="新 FBX 名称", default="")
    overwrite_source_fbx: BoolProperty(
        name="覆盖原 FBX",
        description="导出到源 FBX 路径；默认关闭并生成新文件",
        default=False,
    )
    show_debug_rules: BoolProperty(name="显示分类规则调试", default=False)
    horizontal_cut_angle: FloatProperty(
        name="横面角度容差",
        description="面法线与世界竖直方向的最大夹角",
        default=radians(12.0),
        min=0.0,
        max=radians(45.0),
        subtype="ANGLE",
        unit="ROTATION",
    )
    horizontal_cut_expansion: FloatProperty(
        name="地板轮廓外扩",
        description="切线相对地板边界向外移动的世界空间距离",
        default=0.15,
        min=0.0,
        soft_max=1.0,
        unit="LENGTH",
    )
    horizontal_cut_top_exclusion: FloatProperty(
        name="顶部排除高度",
        description="距离建筑最高点该高度以内的横面不参与切分",
        default=0.15,
        min=0.0,
        soft_max=2.0,
        unit="LENGTH",
    )
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
        footprints = build_interior_footprints(targets, volumes, settings)
        interior = exterior = 0
        for obj in targets:
            if context.mode != "OBJECT":
                bpy.ops.object.mode_set(mode="OBJECT")
            attribute = ensure_attribute(obj.data)
            rule_attribute = ensure_rule_attribute(obj.data)
            class_values = []
            rule_values = []
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
                rule_values.append(RULE_BASE if value == CLASS_INTERIOR else 0)
            propagate_interior_to_adjacent_faces(obj, class_values, rule_values, settings)
            propagate_interior_to_coplanar_faces(
                obj, class_values, rule_values, volumes, settings
            )
            classify_faces_near_interior_volume(
                obj, class_values, rule_values, volumes, footprints, settings
            )
            supplement_low_faces_from_floor_footprints(
                obj, class_values, rule_values, footprints, settings
            )
            supplement_inward_floor_boundary_faces(
                obj, class_values, rule_values, footprints, settings
            )
            classify_horizontal_faces_by_interior_footprint(
                obj, class_values, rule_values, volumes, footprints, settings
            )
            for polygon, value, rule_value in zip(
                obj.data.polygons, class_values, rule_values
            ):
                attribute.data[polygon.index].value = value
                rule_attribute.data[polygon.index].value = rule_value
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


class Bento_OT_debug_rule(Operator):
    bl_idname = "bento.debug_surface_rule"
    bl_label = "独立计算规则"
    bl_options = {"REGISTER", "UNDO"}

    rule_name: EnumProperty(
        items=[
            ("UPWARD", "全部向上面", ""),
            ("FLOOR_OBJECT", "地板对象向上面", ""),
            ("CENTER_IN_VOLUME", "面中心位于室内盒", ""),
            ("RAY_VISIBLE", "仅室内射线可见", ""),
            ("RAY_TOWARD_ORIGIN", "射线面朝室内起点", ""),
            ("RAY_AWAY_FROM_ORIGIN", "射线面背离室内起点", ""),
            ("OUTER_FRAME", "外侧平行门框候选", ""),
            ("RAY_MINUS_OUTER_FRAME", "射线减去外侧门框", ""),
            ("VOLUME_EDGE", "仅室内盒贴边", ""),
            ("FLOOR_FOOTPRINT_SIDE", "仅地板轮廓竖面", ""),
            ("HORIZONTAL_FOOTPRINT", "仅横面轮廓", ""),
        ],
        default="RAY_VISIBLE",
    )

    def execute(self, context):
        if context.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        targets = iter_target_objects(context)
        volumes = iter_volumes(context)
        if not targets:
            self.report({"ERROR"}, "没有可调试的网格对象")
            return {"CANCELLED"}
        settings = context.scene.bento_surface_splitter
        target_bvh, _ = build_target_bvh(context, targets)
        search_distance = calculate_adaptive_search_distance(
            targets, volumes, max(settings.surface_probe_distance, 0.0001)
        )
        volume_samples = [
            point for volume in volumes for point in volume_sample_points(volume)
        ]
        footprints = build_interior_footprints(targets, volumes, settings)
        bpy.ops.object.select_all(action="DESELECT")
        selected_count = 0
        for obj in targets:
            obj.hide_set(False)
            obj.select_set(True)
            context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        for obj in context.objects_in_mode_unique_data:
            bm = bmesh.from_edit_mesh(obj.data)
            bm.faces.ensure_lookup_table()
            bm.faces.layers.int.get(CLASS_ATTRIBUTE)
            for face in bm.faces:
                polygon = obj.data.polygons[face.index]
                face.select = evaluate_debug_rule(
                    self.rule_name,
                    obj,
                    polygon,
                    volumes,
                    volume_samples,
                    footprints,
                    settings,
                    target_bvh,
                    search_distance,
                )
                if face.select:
                    selected_count += 1
            bmesh.update_edit_mesh(obj.data, loop_triangles=False, destructive=False)
        self.report({"INFO"}, f"独立规则命中 {selected_count} 面")
        return {"FINISHED"}


class Bento_OT_preview_horizontal_cuts(Operator):
    bl_idname = "bento.preview_horizontal_floor_cuts"
    bl_label = "选择待切分横面"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        if context.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        targets = iter_target_objects(context)
        settings = context.scene.bento_surface_splitter
        footprints = build_interior_footprints(targets, iter_volumes(context), settings)
        if not footprints:
            self.report({"ERROR"}, "没有可用地板轮廓，请确认地板对象名称")
            return {"CANCELLED"}
        top_z = max(
            (obj.matrix_world @ Vector(corner)).z
            for obj in targets
            for corner in obj.bound_box
        )
        bpy.ops.object.select_all(action="DESELECT")
        for obj in targets:
            obj.hide_set(False)
            obj.select_set(True)
            context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        count = 0
        for obj in context.objects_in_mode_unique_data:
            bm = bmesh.from_edit_mesh(obj.data)
            bm.faces.ensure_lookup_table()
            for face in bm.faces:
                face.select = is_horizontal_cut_candidate(
                    obj, obj.data.polygons[face.index], footprints, settings, top_z
                )
                if face.select:
                    count += 1
            bmesh.update_edit_mesh(obj.data, loop_triangles=False, destructive=False)
        self.report({"INFO"}, f"找到 {count} 个待切分横面")
        return {"FINISHED"}


class Bento_OT_save_horizontal_cut_faces(Operator):
    bl_idname = "bento.save_horizontal_cut_faces"
    bl_label = "保存当前选择为待切分面"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        if context.mode != "EDIT_MESH":
            self.report({"ERROR"}, "请先进入网格编辑模式并选择面")
            return {"CANCELLED"}
        count = 0
        for obj in context.objects_in_mode_unique_data:
            bm = bmesh.from_edit_mesh(obj.data)
            bm.faces.ensure_lookup_table()
            layer = bm.faces.layers.int.get(CUT_FACE_ATTRIBUTE)
            if layer is None:
                layer = bm.faces.layers.int.new(CUT_FACE_ATTRIBUTE)
            for face in bm.faces:
                face[layer] = 1 if face.select else 0
                if face.select:
                    count += 1
            bmesh.update_edit_mesh(obj.data, loop_triangles=False, destructive=False)
        remove_cut_preview()
        self.report({"INFO"}, f"已保存 {count} 个待切分面")
        return {"FINISHED"}


class Bento_OT_select_saved_horizontal_cut_faces(Operator):
    bl_idname = "bento.select_saved_horizontal_cut_faces"
    bl_label = "重新选择已保存面"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        if context.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        targets = iter_target_objects(context)
        bpy.ops.object.select_all(action="DESELECT")
        saved_objects = [
            obj for obj in targets
            if obj.data.attributes.get(CUT_FACE_ATTRIBUTE) is not None
        ]
        if not saved_objects:
            self.report({"WARNING"}, "没有已保存的待切分面")
            return {"CANCELLED"}
        for obj in saved_objects:
            obj.hide_set(False)
            obj.select_set(True)
            context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        count = 0
        for obj in context.objects_in_mode_unique_data:
            bm = bmesh.from_edit_mesh(obj.data)
            bm.faces.ensure_lookup_table()
            layer = bm.faces.layers.int.get(CUT_FACE_ATTRIBUTE)
            for face in bm.faces:
                face.select = layer is not None and bool(face[layer])
                if face.select:
                    count += 1
            bmesh.update_edit_mesh(obj.data, loop_triangles=False, destructive=False)
        self.report({"INFO"}, f"已选择 {count} 个保存面")
        return {"FINISHED"}


class Bento_OT_create_cut_preview(Operator):
    bl_idname = "bento.create_horizontal_cut_preview"
    bl_label = "生成切线预览"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        if context.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        targets = iter_target_objects(context)
        settings = context.scene.bento_surface_splitter
        footprints = build_interior_footprints(targets, iter_volumes(context), settings)
        if not footprints:
            self.report({"ERROR"}, "没有可用地板轮廓，请确认地板对象名称")
            return {"CANCELLED"}
        preview, level_count = create_cut_preview(
            context, footprints, targets, settings
        )
        bpy.ops.object.select_all(action="DESELECT")
        preview.select_set(True)
        context.view_layer.objects.active = preview
        self.report({"INFO"}, f"已生成 {level_count} 层切线预览")
        return {"FINISHED"}


class Bento_OT_clear_cut_preview(Operator):
    bl_idname = "bento.clear_horizontal_cut_preview"
    bl_label = "清除切线预览"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        remove_cut_preview()
        return {"FINISHED"}


class Bento_OT_toggle_cut_preview(Operator):
    bl_idname = "bento.toggle_horizontal_cut_preview"
    bl_label = "显示或隐藏切线预览"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        preview = bpy.data.objects.get(CUT_PREVIEW_NAME)
        if preview is None:
            self.report({"WARNING"}, "尚未生成切线预览")
            return {"CANCELLED"}
        preview.hide_set(not preview.hide_get())
        return {"FINISHED"}


class Bento_OT_apply_horizontal_cuts(Operator):
    bl_idname = "bento.apply_horizontal_floor_cuts"
    bl_label = "按地板轮廓切分横面"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        if context.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        targets = iter_target_objects(context)
        settings = context.scene.bento_surface_splitter
        footprints = build_interior_footprints(targets, iter_volumes(context), settings)
        if not footprints:
            self.report({"ERROR"}, "没有可用地板轮廓，请确认地板对象名称")
            return {"CANCELLED"}
        planes = floor_boundary_planes(
            footprints, settings.horizontal_cut_expansion
        )
        cut_count = 0
        changed_objects = []
        for obj in targets:
            mesh = obj.data
            before_counts = (len(mesh.vertices), len(mesh.edges), len(mesh.polygons))
            bm = bmesh.new()
            bm.from_mesh(mesh)
            bm.faces.ensure_lookup_table()
            cut_layer = bm.faces.layers.int.get(CUT_FACE_ATTRIBUTE)
            candidate_faces = {
                face
                for face in bm.faces
                if cut_layer is not None and bool(face[cut_layer])
            }
            if not candidate_faces:
                bm.free()
                continue
            inverse = obj.matrix_world.inverted_safe()
            normal_inverse = obj.matrix_world.to_3x3().transposed()
            epsilon = max(settings.volume_tolerance * 4.0, 0.0005)
            for axis, value in planes:
                plane_world_co = Vector((value, 0.0, 0.0)) if axis == "X" else Vector((0.0, value, 0.0))
                plane_world_no = Vector((1.0, 0.0, 0.0)) if axis == "X" else Vector((0.0, 1.0, 0.0))
                plane_co = inverse @ plane_world_co
                plane_no = (normal_inverse @ plane_world_no).normalized()
                crossing_faces = {
                    face
                    for face in candidate_faces
                    if face.is_valid
                    and min((obj.matrix_world @ vertex.co)[0 if axis == "X" else 1]
                            for vertex in face.verts) < value - epsilon
                    and max((obj.matrix_world @ vertex.co)[0 if axis == "X" else 1]
                            for vertex in face.verts) > value + epsilon
                }
                geometry = {
                    element
                    for face in crossing_faces
                    for element in (*face.verts, *face.edges, face)
                }
                if not geometry:
                    continue
                bmesh.ops.bisect_plane(
                    bm,
                    geom=list(geometry),
                    plane_co=plane_co,
                    plane_no=plane_no,
                    dist=epsilon,
                    clear_inner=False,
                    clear_outer=False,
                )
            bm.to_mesh(mesh)
            bm.free()
            mesh.update()
            after_counts = (len(mesh.vertices), len(mesh.edges), len(mesh.polygons))
            increase = sum(max(0, after - before) for before, after in zip(before_counts, after_counts))
            if increase:
                cut_count += increase
                changed_objects.append(obj)
        remove_cut_preview()
        if changed_objects:
            bpy.ops.object.select_all(action="DESELECT")
            for obj in changed_objects:
                obj.select_set(True)
                context.view_layer.objects.active = obj
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="DESELECT")
        if not changed_objects:
            self.report({"ERROR"}, "切分未产生任何网格变化，请调整外扩或候选面")
            return {"CANCELLED"}
        self.report({"INFO"}, f"切分已产生 {cut_count} 项网格拓扑变化；未修改分类")
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

        remove_previous_outputs()
        root_collection = bpy.data.collections.get("Bento_BuildingSplit")
        if root_collection is None:
            root_collection = bpy.data.collections.new("Bento_BuildingSplit")
            context.scene.collection.children.link(root_collection)
        root_collection["bento_surface_output_collection"] = True

        interior_parent = ensure_parent_empty(context, settings.interior_name)
        exterior_parent = ensure_parent_empty(context, settings.exterior_name)
        roof_parent = ensure_parent_empty(context, settings.roof_name)
        collections = {
            CLASS_INTERIOR: get_or_create_child_collection(root_collection, settings.interior_name),
            CLASS_EXTERIOR: get_or_create_child_collection(root_collection, settings.exterior_name),
        }
        for collection in collections.values():
            collection["bento_surface_output_collection"] = True
        for obj in context.scene.objects:
            if not is_excluded_object(obj) or is_volume_object(obj) or obj in {interior_parent, exterior_parent, roof_parent}:
                continue
            obj.parent = roof_parent

        created = 0
        sources_to_remove = []
        for source in targets:
            source_created = 0
            for class_value, category_name, parent in (
                (CLASS_INTERIOR, settings.interior_name, interior_parent),
                (CLASS_EXTERIOR, settings.exterior_name, exterior_parent),
            ):
                output_name = compose_output_name(
                    source.name,
                    category_name,
                    settings.name_placement,
                    settings.use_name_separator,
                )
                output = copy_polygon_subset(source, class_value, output_name)
                if output is None:
                    continue
                collections[class_value].objects.link(output)
                output.parent = parent
                created += 1
                source_created += 1
            if source_created and settings.replace_source_objects:
                sources_to_remove.append(source)
        for source in sources_to_remove:
            mesh = source.data
            bpy.data.objects.remove(source, do_unlink=True)
            if mesh.users == 0:
                bpy.data.meshes.remove(mesh)
        if settings.export_after_split and settings.export_path:
            bpy.ops.bento.export_building_fbx()
        action = f"并删除 {len(sources_to_remove)} 个原对象" if sources_to_remove else "，保留原对象"
        self.report({"INFO"}, f"已生成 {created} 个分类对象{action}")
        return {"FINISHED"}


class Bento_OT_export_fbx(Operator):
    bl_idname = "bento.export_building_fbx"
    bl_label = "导出分类 FBX"

    def execute(self, context):
        settings = context.scene.bento_surface_splitter
        path = resolve_export_path(settings)
        if not path:
            self.report({"ERROR"}, "请设置 FBX 输出路径")
            return {"CANCELLED"}
        os.makedirs(os.path.dirname(path), exist_ok=True)
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
        box.label(text="3. 网格预处理（不修改分类）")
        box.prop(settings, "horizontal_cut_angle")
        box.prop(settings, "horizontal_cut_expansion")
        box.prop(settings, "horizontal_cut_top_exclusion")
        row = box.row(align=True)
        row.operator("bento.preview_horizontal_floor_cuts", text="选择自动候选")
        row.operator("bento.save_horizontal_cut_faces", text="保存当前选择")
        row = box.row(align=True)
        row.operator("bento.select_saved_horizontal_cut_faces", text="重新选择已保存面")
        row.operator("bento.create_horizontal_cut_preview", text="生成切线预览")
        row = box.row(align=True)
        row.operator("bento.toggle_horizontal_cut_preview", text="隐藏/显示预览")
        row.operator("bento.clear_horizontal_cut_preview", text="清除预览")
        box.operator("bento.apply_horizontal_floor_cuts", text="按已保存面执行切分")

        box = layout.box()
        box.label(text="4. 自动分类")
        box.prop(settings, "upward_normal_threshold")
        box.prop(settings, "surface_probe_distance")
        box.prop(settings, "volume_tolerance")
        box.operator("bento.classify_building_surfaces", icon="MOD_BUILD")

        box = layout.box()
        box.label(text="5. 查看分类结果")
        row = box.row(align=True)
        op = row.operator("bento.select_surface_class", text="选择室内面")
        op.class_value = "1"
        op = row.operator("bento.select_surface_class", text="选择外墙面")
        op.class_value = "2"
        box.label(text="这里只读取面上的分类标记", icon="INFO")

        box = layout.box()
        box.label(text="6. 手动修补")
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
        box.label(text="7. 生成分类对象与导出")
        box.prop(settings, "interior_name")
        box.prop(settings, "exterior_name")
        box.prop(settings, "roof_name")
        box.prop(settings, "name_placement")
        box.prop(settings, "use_name_separator")
        box.prop(settings, "replace_source_objects")
        box.operator("bento.split_building_surfaces", icon="MOD_EXPLODE")
        box.prop(settings, "overwrite_source_fbx")
        if settings.overwrite_source_fbx:
            box.prop(settings, "source_fbx_path")
        else:
            box.prop(settings, "output_fbx_name")
            box.prop(settings, "export_path")
        box.prop(settings, "export_after_split")
        box.operator("bento.export_building_fbx", icon="EXPORT")

        debug_box = layout.box()
        debug_box.prop(
            settings,
            "show_debug_rules",
            text="分类规则调试",
            icon="TRIA_DOWN" if settings.show_debug_rules else "TRIA_RIGHT",
            emboss=False,
        )
        if settings.show_debug_rules:
            debug_box.label(text="每个按钮均从原始几何独立重算")
            for rule_name, label in (
                ("UPWARD", "全部向上面"),
                ("FLOOR_OBJECT", "仅地板对象向上面"),
                ("CENTER_IN_VOLUME", "仅面中心位于室内盒"),
                ("RAY_VISIBLE", "射线命中（忽略法线）"),
                ("RAY_TOWARD_ORIGIN", "射线命中且面朝室内"),
                ("RAY_AWAY_FROM_ORIGIN", "射线命中且面背离室内"),
                ("OUTER_FRAME", "仅外侧平行门框候选"),
                ("RAY_MINUS_OUTER_FRAME", "射线室内减去外侧门框"),
                ("VOLUME_EDGE", "仅室内盒贴边"),
                ("FLOOR_FOOTPRINT_SIDE", "仅地板轮廓竖面"),
                ("HORIZONTAL_FOOTPRINT", "仅横面轮廓"),
            ):
                op = debug_box.operator("bento.debug_surface_rule", text=label)
                op.rule_name = rule_name


CLASSES = (
    BentoSurfaceSplitterSettings,
    Bento_OT_create_interior_volume,
    Bento_OT_mark_volume,
    Bento_OT_toggle_excluded,
    Bento_OT_exclude_and_hide,
    Bento_OT_classify,
    Bento_OT_select_class,
    Bento_OT_debug_rule,
    Bento_OT_preview_horizontal_cuts,
    Bento_OT_save_horizontal_cut_faces,
    Bento_OT_select_saved_horizontal_cut_faces,
    Bento_OT_create_cut_preview,
    Bento_OT_clear_cut_preview,
    Bento_OT_toggle_cut_preview,
    Bento_OT_apply_horizontal_cuts,
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
