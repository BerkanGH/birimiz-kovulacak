using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace BirimizKovulacak.Game
{
    /// <summary>Crops a UI element out of the final frame and writes a PNG to Pictures/BirimizKovulacak.</summary>
    public static class ScreenshotSaver
    {
        public static string Directory
        {
            get
            {
                string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (string.IsNullOrEmpty(pics)) pics = Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? Application.persistentDataPath, "Pictures");
                return Path.Combine(pics, "BirimizKovulacak");
            }
        }

        public static IEnumerator Capture(VisualElement target, string fileStem, Action<string> done)
        {
            yield return null; // let hidden buttons disappear
            yield return new WaitForEndOfFrame();
            Texture2D shot = null, crop = null;
            string path = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                Rect wb = target.worldBound;
                float panelWidth = target.panel != null ? target.panel.visualTree.worldBound.width : UnityEngine.Screen.width;
                float scale = panelWidth > 0 ? shot.width / panelWidth : 1f;
                int x = Mathf.Clamp(Mathf.RoundToInt(wb.x * scale), 0, shot.width - 1);
                int w = Mathf.Clamp(Mathf.RoundToInt(wb.width * scale), 1, shot.width - x);
                int h = Mathf.Clamp(Mathf.RoundToInt(wb.height * scale), 1, shot.height);
                int y = Mathf.Clamp(shot.height - Mathf.RoundToInt(wb.yMax * scale), 0, shot.height - h);
                crop = new Texture2D(w, h, TextureFormat.RGB24, false);
                crop.SetPixels(shot.GetPixels(x, y, w, h));
                crop.Apply();
                byte[] png = crop.EncodeToPNG();
                string dir = Directory;
                System.IO.Directory.CreateDirectory(dir);
                path = Path.Combine(dir, fileStem + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                File.WriteAllBytes(path, png);
                MatchLog.Write("saved letter " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI] Could not save screenshot: " + e.Message);
                path = null;
            }
            finally
            {
                if (shot != null) UnityEngine.Object.Destroy(shot);
                if (crop != null) UnityEngine.Object.Destroy(crop);
            }
            done?.Invoke(path);
        }
    }
}
