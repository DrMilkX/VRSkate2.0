using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch fixes for the CharacterController "drift" caused by convex MeshColliders.
///
/// A convex MeshCollider is a single inflated convex HULL around the mesh, so concave
/// shapes (hedges, L-shaped sidewalks, curbs) bulge outward into invisible ramps/walls.
/// A CharacterController sliding against those hulls under gravity gets pushed sideways,
/// which reads as a one-directional drift that deflects off geometry.
///
/// All actions here work on prefab INSTANCES (they only override properties or disable /
/// add components, never destroy prefab-sourced components) and are fully undoable.
/// If nothing is selected they run on every MeshCollider in the open scene(s); if you
/// select one or more objects they run only on those objects and their children.
///
/// Recommended order: try "Make Non-Convex" first (keeps exact collision, kills the bulge).
/// Fall back to "Replace with Box" or "Disable" if you want simpler / no collision.
/// </summary>
public static class MeshColliderTools
{
    private const string Menu = "Tools/VRSkate/Colliders/";

    // -------------------------------------------------------------------------
    // Target gathering
    // -------------------------------------------------------------------------

    private static List<MeshCollider> GatherTargets(out string scope)
    {
        var result = new List<MeshCollider>();

        if (Selection.gameObjects != null && Selection.gameObjects.Length > 0)
        {
            scope = $"{Selection.gameObjects.Length} selected object(s) + children";
            foreach (var go in Selection.gameObjects)
                result.AddRange(go.GetComponentsInChildren<MeshCollider>(true));
        }
        else
        {
            scope = "the entire open scene";
            result.AddRange(Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        }

        // De-duplicate (a selection can overlap parents/children).
        return new List<MeshCollider>(new HashSet<MeshCollider>(result));
    }

    private static bool NeedsConvex(MeshCollider mc)
    {
        // MeshColliders that are triggers, or that ride a non-kinematic Rigidbody, are
        // REQUIRED to be convex by the physics engine. Leave those alone.
        if (mc.isTrigger) return true;
        var rb = mc.attachedRigidbody;
        return rb != null && !rb.isKinematic;
    }

    private static void MarkDirty(Object o)
    {
        EditorUtility.SetDirty(o);
        var comp = o as Component;
        if (comp != null && !EditorApplication.isPlaying)
            EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);
    }

    // -------------------------------------------------------------------------
    // Report
    // -------------------------------------------------------------------------

    [MenuItem(Menu + "Report Mesh Colliders", priority = 0)]
    private static void Report()
    {
        var targets = GatherTargets(out string scope);
        int convex = 0, triggers = 0, onRigidbody = 0, disabled = 0;
        foreach (var mc in targets)
        {
            if (mc.convex) convex++;
            if (mc.isTrigger) triggers++;
            if (mc.attachedRigidbody != null) onRigidbody++;
            if (!mc.enabled) disabled++;
        }

        Debug.Log($"[MeshColliderTools] Scanned {scope}.\n" +
                  $"  MeshColliders: {targets.Count}\n" +
                  $"  convex = true: {convex}\n" +
                  $"  isTrigger:     {triggers}\n" +
                  $"  on Rigidbody:  {onRigidbody}\n" +
                  $"  disabled:      {disabled}");
    }

    // -------------------------------------------------------------------------
    // Make non-convex  (recommended fix — keeps exact collision, removes the bulge)
    // -------------------------------------------------------------------------

    [MenuItem(Menu + "Make Non-Convex (recommended)", priority = 20)]
    private static void MakeNonConvex()
    {
        var targets = GatherTargets(out string scope);
        if (!Confirm("Make Mesh Colliders Non-Convex",
                     $"Set convex = false on non-trigger, non-dynamic MeshColliders in {scope}.\n\n" +
                     "This keeps exact collision shape and removes the convex-hull bulge that causes the drift.",
                     targets.Count))
            return;

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Make MeshColliders Non-Convex");

        int changed = 0, skipped = 0;
        foreach (var mc in targets)
        {
            if (NeedsConvex(mc)) { skipped++; continue; }
            if (!mc.convex) continue;

            Undo.RecordObject(mc, "Make MeshCollider Non-Convex");
            mc.convex = false;
            MarkDirty(mc);
            changed++;
        }

        Undo.CollapseUndoOperations(group);
        Debug.Log($"[MeshColliderTools] Set convex=false on {changed} MeshCollider(s); " +
                  $"left {skipped} that must stay convex (triggers / dynamic Rigidbodies).");
    }

    // -------------------------------------------------------------------------
    // Replace with Box  (adds a BoxCollider sized to the mesh bounds, disables the MeshCollider)
    // -------------------------------------------------------------------------

    [MenuItem(Menu + "Replace with Box Colliders", priority = 40)]
    private static void ReplaceWithBox()
    {
        var targets = GatherTargets(out string scope);
        if (!Confirm("Replace Mesh Colliders with Box Colliders",
                     $"Add a BoxCollider (sized to the mesh bounds) and DISABLE the MeshCollider on objects in {scope}.\n\n" +
                     "The MeshCollider is disabled rather than deleted so this works on prefab instances and can be undone. " +
                     "Note: a single box can wrap complex shapes (e.g. a whole tree canopy).",
                     targets.Count))
            return;

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Replace MeshColliders with BoxColliders");

        int replaced = 0, skipped = 0;
        foreach (var mc in targets)
        {
            if (!mc.enabled) { skipped++; continue; }               // already handled
            if (mc.sharedMesh == null) { skipped++; continue; }      // nothing to size a box from

            Bounds b = mc.sharedMesh.bounds;                          // local space of this transform

            var box = Undo.AddComponent<BoxCollider>(mc.gameObject);
            box.center = b.center;
            box.size = b.size;
            box.isTrigger = mc.isTrigger;
            box.sharedMaterial = mc.sharedMaterial;

            Undo.RecordObject(mc, "Disable MeshCollider");
            mc.enabled = false;

            MarkDirty(mc);
            replaced++;
        }

        Undo.CollapseUndoOperations(group);
        Debug.Log($"[MeshColliderTools] Added {replaced} BoxCollider(s) and disabled those MeshColliders; " +
                  $"skipped {skipped} (already disabled or no mesh).");
    }

    // -------------------------------------------------------------------------
    // Disable  (removes collision entirely; player passes through)
    // -------------------------------------------------------------------------

    [MenuItem(Menu + "Disable Mesh Colliders (no collision)", priority = 60)]
    private static void DisableColliders()
    {
        var targets = GatherTargets(out string scope);
        if (!Confirm("Disable Mesh Colliders",
                     $"Disable every MeshCollider in {scope} so the player passes through them.\n\n" +
                     "Disabled (not deleted) so it works on prefab instances and can be undone.",
                     targets.Count))
            return;

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Disable MeshColliders");

        int changed = 0;
        foreach (var mc in targets)
        {
            if (!mc.enabled) continue;
            Undo.RecordObject(mc, "Disable MeshCollider");
            mc.enabled = false;
            MarkDirty(mc);
            changed++;
        }

        Undo.CollapseUndoOperations(group);
        Debug.Log($"[MeshColliderTools] Disabled {changed} MeshCollider(s).");
    }

    // -------------------------------------------------------------------------

    private static bool Confirm(string title, string body, int count)
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog(title, "Exit Play mode before running collider tools.", "OK");
            return false;
        }
        if (count == 0)
        {
            EditorUtility.DisplayDialog(title, "No MeshColliders found for the current scope.", "OK");
            return false;
        }
        return EditorUtility.DisplayDialog(title, $"{body}\n\nAffected MeshColliders: {count}\n\nThis can be undone (Ctrl+Z).", "Do it", "Cancel");
    }
}
