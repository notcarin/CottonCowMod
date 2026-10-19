#if COTTONCOW_TESTS
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using TotS;
using TotS.DateTime;
using TotS.DateTime.Weather;
using UnityEngine;



namespace CottonCowMod.Testing
{
    /// <summary>
    /// The "photos" suite: stages beauty shots of the cows for the mod page. Run it on a save
    /// with both cows delivered and the trough placed. Shots go to TestResults/photos/ at 2x
    /// window resolution with the HUD hidden. Several camera angles are taken per shot so the
    /// best can be picked by eye.
    ///
    /// Uses its own virtual camera (PhotoCamera) registered with the game's CameraManager,
    /// which takes over from the player camera and holds the pose we set.
    /// </summary>
    internal static class PhotoSuite
    {
        private const int SuperSize = 2;
        private const float RadToDeg = 57.29578f;
        private static readonly float[] Azimuths = { 0f, 60f, 120f, 180f, 240f, 300f };

        private static string _dir;
        private static readonly List<Canvas> _hiddenCanvases = new List<Canvas>();

        public static IEnumerator Run(TestRunner t)
        {
            yield return t.LoadSave(t.Setting("save", ""));
            yield return t.WaitUntil("mod sees save ready", () => PlayerCows.SaveReady, 30f);
            yield return t.WaitUntil("cows spawned", () => CowSpawner.Cow1Spawned && CowSpawner.Cow2Spawned, 60f);
            if (!t.Check("glade found", GladeArea.Built))
                yield break;

            _dir = Path.Combine(TestRunner.ResultsDir, "photos");
            Directory.CreateDirectory(_dir);

            // Set dressing: food in the trough, a "milk ready" marker on one cow, dry weather
            var trough = CowTroughManager.Instance.GetInventory();
            foreach (string food in new[] { "Ingredient_Apple", "Ingredient_Carrot", "Ingredient_Pumpkin_Orange", "Ingredient_Cabbage" })
                TestHelpers.AddToInventory(trough, food, 3, 3);
            var milkCow = TestHelpers.CowTag(false);
            if (milkCow != null)
            {
                milkCow.HasMilk = true;
                milkCow.MilkQuality = 3;
                milkCow.GetComponent<CowMilkIndicator>()?.SetMilkAvailable(true);
            }
            Singleton<DateTimeManager>.Instance.SetWeatherConditionOverride(WeatherCondition.Dry);

            if (!t.Check("photo camera registered", EnableFreeCamera()))
                yield break;
            yield return new WaitForSeconds(1f);

            var troughGO = Object.FindObjectOfType<CowTroughInteraction>()?.gameObject;
            t.Log(troughGO != null ? $"Trough at {troughGO.transform.position}" : "No placed trough found");

            foreach (var (label, hour, minute) in new[] { ("morning", 9, 0), ("midday", 13, 0), ("golden", 18, 30) })
            {
                SetTime(hour, minute);
                yield return new WaitForSeconds(3f);

                Vector3 cow1 = TestHelpers.Cow(true).transform.position;
                Vector3 cow2 = TestHelpers.Cow(false).transform.position;
                Vector3 between = (cow1 + cow2) / 2f;

                // Wide: both cows in the glade
                yield return Angles(t, $"{label}-wide", between + Vector3.up * 0.8f, 10f, 3.5f);

                // Close-up of the cow with milk ready
                Vector3 milkPos = TestHelpers.Cow(false).transform.position;
                yield return Angles(t, $"{label}-cow", milkPos + Vector3.up * 1.0f, 3.8f, 1.2f);

                // The trough with food
                if (troughGO != null)
                    yield return Angles(t, $"{label}-trough", troughGO.transform.position + Vector3.up * 0.4f, 2.6f, 1.6f);

                // The hobbit with her cows
                Vector3 cowNow = TestHelpers.Cow(true).transform.position;
                Vector3 toCenter = (GladeArea.Center - cowNow).normalized;
                Vector3 hobbitPos = cowNow + toCenter * 2.2f;
                if (UnityEngine.AI.NavMesh.SamplePosition(hobbitPos, out var hit, 2f, -1))
                    hobbitPos = hit.position;
                Player.Instance.Movement.WarpImmediately(hobbitPos,
                    Quaternion.LookRotation(new Vector3(cowNow.x - hobbitPos.x, 0f, cowNow.z - hobbitPos.z)));
                yield return new WaitForSeconds(1.5f);
                yield return Angles(t, $"{label}-hobbit", (hobbitPos + cowNow) / 2f + Vector3.up * 0.9f, 6f, 1.8f);
            }

            DisableFreeCamera();
            t.Log($"Photos saved to {_dir}");
        }

        /// <summary>
        /// Shoots a subject from several directions, skipping angles where something solid
        /// blocks the view.
        /// </summary>
        private static IEnumerator Angles(TestRunner t, string name, Vector3 target, float distance, float height)
        {
            int taken = 0;
            foreach (float azimuth in Azimuths)
            {
                Vector3 dir = Quaternion.Euler(0f, azimuth, 0f) * Vector3.forward;
                Vector3 pos = target + dir * distance + Vector3.up * height;
                if (!ClearView(pos, target))
                    continue;

                PoseCamera(pos, target);
                yield return new WaitForSeconds(0.6f);
                yield return Capture($"{name}-{azimuth:000}");
                taken++;
            }
            t.Log($"{name}: {taken} angle(s)");
        }

        /// <summary>
        /// True when nothing solid blocks the view, or the first thing hit is the subject itself
        /// (within 1.5m of the target) rather than a tree or wall in front of it.
        /// </summary>
        private static bool ClearView(Vector3 from, Vector3 target)
        {
            Vector3 dir = target - from;
            if (!Physics.Raycast(from, dir.normalized, out RaycastHit hit, dir.magnitude, ~0, QueryTriggerInteraction.Ignore))
                return true;
            return Vector3.Distance(hit.point, target) < 1.5f;
        }

        /// <summary>
        /// Renders the game camera at SuperSize x the window resolution (the HUD isn't drawn
        /// by the camera, so it's left out), plus a plain screen capture with the HUD hidden
        /// in case the off-screen render differs from what's on screen.
        /// </summary>
        private static IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            RenderHighRes(Path.Combine(_dir, name + ".png"));

            SetHudVisible(false);
            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, name + "-screen.png"));
            yield return null;
            yield return null;
            SetHudVisible(true);
        }

        private static void RenderHighRes(string path)
        {
            var camera = CameraManager.Instance.MainCamera;
            int width = Screen.width * SuperSize;
            int height = Screen.height * SuperSize;
            var target = new RenderTexture(width, height, 24);
            var previous = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previous;

            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, image.EncodeToPNG());

            Object.Destroy(image);
            target.Release();
            Object.Destroy(target);
        }

        private static void SetHudVisible(bool visible)
        {
            if (!visible)
            {
                _hiddenCanvases.Clear();
                foreach (var canvas in Object.FindObjectsOfType<Canvas>())
                {
                    if (canvas.enabled && canvas.isRootCanvas && canvas.renderMode != RenderMode.WorldSpace)
                    {
                        canvas.enabled = false;
                        _hiddenCanvases.Add(canvas);
                    }
                }
            }
            else
            {
                foreach (var canvas in _hiddenCanvases)
                {
                    if (canvas != null)
                        canvas.enabled = true;
                }
                _hiddenCanvases.Clear();
            }
        }

        private static void SetTime(int hour, int minute)
        {
            var dtm = Singleton<DateTimeManager>.Instance;
            var now = dtm.CurrentDateTime;
            // fireDateTimeChanged: false so the mod's morning logic and milk production don't run
            dtm.OverrideTimeWithoutSleepSequence(new ShireDateTime(now.Date, new ShireTime(hour, minute)),
                updateWeatherCondition: false, fireDateTimeChanged: false);
        }

        private static PhotoCamera _camera;

        private static bool EnableFreeCamera()
        {
            if (CameraManager.Instance == null || CameraManager.Instance.MainCamera == null)
                return false;
            _camera = new PhotoCamera(CameraManager.Instance.MainCamera);
            CameraManager.Instance.AddCamera(_camera);
            return true;
        }

        private static void DisableFreeCamera()
        {
            if (_camera != null)
                CameraManager.Instance.RemoveCamera(_camera);
            _camera = null;
        }

        private static void PoseCamera(Vector3 position, Vector3 lookAt)
        {
            Vector3 dir = lookAt - position;
            float yaw = Mathf.Atan2(dir.x, dir.z) * RadToDeg;
            float pitch = -Mathf.Asin(dir.y / dir.magnitude) * RadToDeg;
            _camera.Position = position;
            _camera.Rotation = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right);
            _camera.FocusDistance = dir.magnitude;
        }

        /// <summary>
        /// A virtual camera for the game's CameraManager that holds whatever pose we give it.
        /// Registered with the highest priority, it takes over from the player camera, cutting
        /// in instantly. (The game's DebugFreeCamera isn't in release builds.)
        /// </summary>
        private class PhotoCamera : IVirtualCamera
        {
            public PhotoCamera(Camera reference)
            {
                Position = reference.transform.position;
                Rotation = reference.transform.rotation;
                FocusDistance = reference.focusDistance;
                FocalLength = reference.focalLength;
                Aperture = reference.aperture;
            }

            public string Name => "CottonCowPhotoCamera";
            public bool IsActive => true;
            public int Priority => int.MaxValue;
            public CameraTransitionInfo Transition { get; } = new CameraTransitionInfo(true, true, 0f, false);
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; }
            public float FieldOfView => 40f;
            public float NearClipPlane => 0.1f;
            public float FocusDistance { get; set; }
            public float FocalLength { get; }
            public float Aperture { get; }

            public void Activate(CameraManager.PreviousCameraInfo previousCamera) { }
            public void Deactivate() { }
            public void CameraUpdate(bool cut) { }
        }
    }
}
#endif
