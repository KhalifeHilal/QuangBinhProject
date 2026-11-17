using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Same PNG size and overhead view as the tutorial; frames the main map only.</summary>
public static class MainSceneTopDownCapture
{
    const int Resolution = 1024;

    public static bool TryWrite(string path, Scene scene, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != "MAP" || !root.activeInHierarchy) continue;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is LineRenderer || renderer is TrailRenderer) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
        }
        if (!found)
        {
            Debug.LogWarning("Main-scene screenshot: active MAP renderers were not found.");
            return false;
        }

        GameObject cameraObject = null;
        RenderTexture target = null;
        Texture2D image = null;
        RenderTexture previous = RenderTexture.active;
        var hiddenCanvases = new List<Canvas>();
        var hiddenLines = new List<Renderer>();
        try
        {
            cameraObject = new GameObject("__MainSceneStudyTopDownCapture");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.aspect = 1f;
            camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.06f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.07f, .09f, .11f, 1f);
            camera.cullingMask = ~0;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = Mathf.Max(1000f, bounds.size.magnitude * 4f);
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.transform.SetPositionAndRotation(
                new Vector3(bounds.center.x, bounds.max.y + Mathf.Max(100f, bounds.size.magnitude), bounds.center.z),
                Quaternion.Euler(90f, 0f, 0f));
            target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            target.Create();
            camera.targetTexture = target;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>())
                    if (canvas.enabled) { canvas.enabled = false; hiddenCanvases.Add(canvas); }
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                    if (renderer.enabled && (renderer is LineRenderer || renderer is TrailRenderer))
                    { renderer.enabled = false; hiddenLines.Add(renderer); }
            }
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(Resolution, Resolution, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0, false);
            image.Apply(false, false);
            File.WriteAllBytes(path, image.EncodeToPNG());
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Main-scene screenshot failed: " + exception.Message);
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            foreach (Canvas canvas in hiddenCanvases) if (canvas != null) canvas.enabled = true;
            foreach (Renderer renderer in hiddenLines) if (renderer != null) renderer.enabled = true;
            if (image != null) UnityEngine.Object.Destroy(image);
            if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
            if (cameraObject != null) UnityEngine.Object.Destroy(cameraObject);
        }
    }
}
