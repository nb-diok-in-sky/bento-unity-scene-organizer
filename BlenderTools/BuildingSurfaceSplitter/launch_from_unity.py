import bpy
import os
import sys


def arguments_after_separator():
    return [] if "--" not in sys.argv else sys.argv[sys.argv.index("--") + 1:]


def enable_addon(addon_path):
    addon_path = os.path.abspath(addon_path)
    parent = os.path.dirname(addon_path)
    module_name = os.path.basename(addon_path)
    if parent not in sys.path:
        sys.path.insert(0, parent)
    try:
        bpy.ops.preferences.addon_enable(module=module_name)
    except Exception:
        __import__(module_name)
        module = sys.modules[module_name]
        if not hasattr(bpy.types.Scene, "bento_surface_splitter"):
            module.register()
    if not hasattr(bpy.types.Scene, "bento_surface_splitter"):
        module = sys.modules.get(module_name) or __import__(module_name)
        module.register()


def start_local_debug_bridge():
    try:
        bpy.ops.preferences.addon_enable(module="bld_remote_mcp")
        import bld_remote
        if not bld_remote.is_mcp_service_up():
            bld_remote.start_mcp_service()
        print(f"[Bento] Blender debug bridge listening on {bld_remote.get_mcp_service_port()}")
    except Exception as exception:
        print(f"[Bento] Blender debug bridge unavailable: {exception}")


def main():
    args = arguments_after_separator()
    if len(args) < 2:
        raise RuntimeError("Expected FBX path and addon path after --")
    fbx_path, addon_path = map(os.path.abspath, args[:2])
    if not os.path.isfile(fbx_path):
        raise RuntimeError(f"FBX not found: {fbx_path}")
    enable_addon(addon_path)
    start_local_debug_bridge()
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    before = set(bpy.context.scene.objects)
    try:
        bpy.ops.wm.fbx_import(filepath=fbx_path)
    except Exception:
        bpy.ops.import_scene.fbx(filepath=fbx_path)
    imported = [obj for obj in bpy.context.scene.objects if obj not in before]
    bpy.ops.object.select_all(action="DESELECT")
    for obj in imported:
        obj.select_set(True)
        if obj.type == "MESH":
            obj["bento_surface_target"] = True
    if imported:
        bpy.context.view_layer.objects.active = imported[0]
    settings = bpy.context.scene.bento_surface_splitter
    settings.target_scope = "IMPORTED"
    settings.export_path = os.path.splitext(fbx_path)[0] + "_processed.fbx"
    print(f"[Bento] Imported {len(imported)} objects from {fbx_path}")


main()
