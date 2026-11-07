using System;
using Audio.SpatialSystem;
using EFT;
using UnityEngine;

namespace RealSoundSimulation.Acoustics
{
    // Enclosure probe (roadmap step 1, 0.2.2). Measures the space around you with short rays, against the same collision
    // layers the game's occlusion uses (buildings, terrain; not players or small props).
    //
    // Every 0.1 s: RayCount rays spread evenly over a sphere from your head. Each ray that hits within MaxRange counts as
    // "closed" with a weight that falls off with distance (full at NearFull, none at MaxRange), so a wall at 2 m counts more
    // than a building 25 m away. Rays pointing more than 10 deg down are left out of the scores (the ground is always
    // there), but their hits are still kept for the echo stage.
    // Ground (0.2.3): a hit on a floor-like surface (normal more than ~45 deg upward) counts as open, not enclosed. Before
    // 0.2.3 the ground just below level counted as a wall, so open ground read 0.2-0.4 and every own shot outdoors got
    // 15-25% of the indoor recording (raid 6). Ground hits are still kept for the echo stage.
    //   Enclosure   0..1  weighted share of the level-and-up rays that hit (open field ~0, room ~0.9+)
    //   Horizontal  0..1  same, only rays within 30 deg of level      (walls around you)
    //   Overhead    0..1  same, only rays more than 45 deg up         (roof/overhang)
    //   MeanHit     m     average distance of the rays that hit
    // On demand (when you fire): AimCone rays in a 25 deg cone along where you're looking -> AimEnclosure and AimDistance.
    //
    // Room measure (0.3.1), used by fix 3c. Raid 7: a covered bench (roof, one wall, open sides) read 0.75-0.85 enclosed
    // because the roof alone filled the upward rays, and a small glass office read 0.97 (windows looked like concrete).
    // A reverberant room needs walls AND a roof, so:
    //   per ray closure = RoomWeight(distance) x Surfaces.Closure(material)   (1 within 10 m, 0 at 40 m; glass/planks partial)
    //   HorizClosure    = mean over rays within 30 deg of level                (walls)
    //   OverClosure     = mean over rays more than 30 deg up                   (roof)
    //   ZenithClosure   = 5 rays straight up and tilted 20 deg (0.3.2): is there a roof right above your head?
    //   RoofClosure     = min(OverClosure, ZenithClosure). Raid 8: 1 m outside a kiosk's door the angled upward rays
    //                     still caught its roof edge (OverClosure 0.58-0.84), so shots outside got ~40% indoor
    //                     recording. The zenith test fades the roof out over about 1 m past the roofline.
    //   Room            = HorizClosure x RoofClosure
    //   SizeFactor      = 0.5 .. 1 from MeanHit 2 .. 6 m: the game's indoor recordings carry a long room tail; a tiny space
    //                     (furniture, walls 2 m away) has a short one, so it gets less of the indoor recording and the
    //                     echoes (fix 6) supply its dense early reflections.
    //   ThinShare       = share of the closure that came from thin/see-through surfaces (for the logs)
    //   AimClosure      = the same closure, averaged over the aim cone
    //
    // The hits (direction, distance, surface) are kept for the echo stage (roadmap step 4). Cost: 32 raycasts per 0.1 s.
    internal class EnclosureProbe : MonoBehaviour
    {
        public const int RayCount = 32;
        public const float MaxRange = 40f;
        public const float NearFull = 3f;
        private const float Interval = 0.1f;
        private const float AimHalfAngle = 25f;

        public static bool Valid { get; private set; }
        public static float Enclosure = float.NaN, Horizontal = float.NaN, Overhead = float.NaN, MeanHit = float.NaN;
        public static float HorizClosure = float.NaN, OverClosure = float.NaN, Room = float.NaN, SizeFactor = float.NaN, ThinShare = float.NaN;
        public static float AimClosure = float.NaN;
        public static float ZenithClosure = float.NaN, RoofClosure = float.NaN;
        // 0.3.3: share of the (distance-weighted, non-ground) hits that are hard bare surfaces / glass; HardRoom = how much
        // this is a closed, hard room (stairwell, bare concrete corridor) 0..1.
        public static float HardShare = float.NaN, GlassShare = float.NaN, HardRoom = float.NaN, MuzzleHard = float.NaN;
        public const float HardRoomFrom = 0.5f, HardRoomFull = 0.85f;
        private const float ZenithTiltDeg = 20f;
        private static readonly Vector3[] ZenithDirs = BuildZenith();
        public const float RoomFull = 10f;
        public const float SizeSmallM = 2f, SizeFullM = 6f, SizeSmallFactor = 0.5f;
        public static readonly EFT.Ballistics.MaterialType[] HitMat = new EFT.Ballistics.MaterialType[RayCount];
        public static Vector3 LastOrigin;

        // Per-ray results from the last sweep (world directions; distance = +inf when nothing was hit).
        public static readonly Vector3[] Dirs = new Vector3[RayCount];
        public static readonly float[] HitDist = new float[RayCount];
        public static readonly Vector3[] HitNormal = new Vector3[RayCount];
        public static readonly Vector3[] HitPoint = new Vector3[RayCount];
        public static readonly Collider[] HitCollider = new Collider[RayCount];
        public static float LastSweepTime = -1f;

        // Aim cone hits from the last MeasureAim (for the echo stage).
        public const int AimCone = 9;
        public static readonly Vector3[] AimDirs = new Vector3[AimCone];
        public static readonly float[] AimHitDist = new float[AimCone];
        public static readonly Vector3[] AimHitNormal = new Vector3[AimCone];
        public static readonly Vector3[] AimHitPoint = new Vector3[AimCone];
        public static readonly Collider[] AimHitCollider = new Collider[AimCone];
        private static int _aimFrame = -1;
        private static Vector3 _aimOrigin, _aimForward;
        private static float _aimEnc = float.NaN, _aimDist = float.NaN;

        // Floor-like surface (ground, floors, gentle slopes): open for the enclosure scores.
        public const float GroundNormalY = 0.7f;
        public static bool IsGround(Vector3 normal) => normal.y > GroundNormalY;

        private static readonly Vector3[] UnitDirs = BuildFibonacciSphere(RayCount);
        private float _next;

        private void Update()
        {
            bool inRaid = SpatialAudioSystem.Initialized && MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated && GamePlayerOwner.MyPlayer != null;
            if (!inRaid) { Valid = false; Enclosure = Horizontal = Overhead = MeanHit = HorizClosure = OverClosure = Room = SizeFactor = ThinShare = ZenithClosure = RoofClosure = HardShare = GlassShare = HardRoom = float.NaN; return; }
            if (!Plugin.ProbeOn.Value) { Valid = false; return; }
            if (Time.time < _next) return;
            _next = Time.time + Interval;
            try { SweepNow(); }
            catch (Exception e) { Diagnostics.DiagRunner.ReportOnce("enclosure probe", e); }
        }

        // Runs a sweep now (also used by the echo stage when the probe setting is off or the last sweep is old).
        public static bool SweepNow()
        {
            Diagnostics.Perf.Begin();
            try { return SweepNowCore(); }
            finally { Diagnostics.Perf.End(Diagnostics.Perf.Part.EnclosureSweep); }
        }

        private static bool SweepNowCore()
        {
            if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return false;
            Sweep(MonoBehaviourSingleton<SpatialAudioSystem>.Instance);
            return Valid;
        }

        private static void Sweep(SpatialAudioSystem sas)
        {
            Transform lt = sas.ListenerTransform;
            if (lt == null) return;
            int mask = sas.OcclusionSettings != null ? sas.OcclusionSettings.commonSettings.occlusionLayerMask.value : Physics.DefaultRaycastLayers;
            Vector3 o = lt.position;
            LastOrigin = o;

            float all = 0f, allN = 0f, hor = 0f, horN = 0f, up = 0f, upN = 0f, hitSum = 0f; int hitN = 0;
            float hc = 0f, hcN = 0f, oc = 0f, ocN = 0f, closSum = 0f, thinSum = 0f, hardSum = 0f, glassSum = 0f;
            for (int i = 0; i < RayCount; i++)
            {
                Vector3 d = UnitDirs[i];
                Dirs[i] = d;
                float w = 0f, clos = 0f;
                HitMat[i] = EFT.Ballistics.MaterialType.None;
                if (CountedRay.Raycast(o, d, out RaycastHit hit, MaxRange, mask, QueryTriggerInteraction.Ignore))
                {
                    HitDist[i] = hit.distance;
                    HitNormal[i] = hit.normal;
                    HitPoint[i] = hit.point;
                    HitCollider[i] = hit.collider;
                    HitMat[i] = Surfaces.Material(hit.collider);
                    if (!IsGround(hit.normal))
                    {
                        w = Weight(hit.distance);
                        hitSum += hit.distance; hitN++;
                        float c = Surfaces.Closure(HitMat[i]);
                        clos = RoomWeight(hit.distance) * c;
                        if (Surfaces.IsThin(HitMat[i])) thinSum += RoomWeight(hit.distance) * (1f - c);
                        if (Surfaces.IsHard(HitMat[i])) hardSum += RoomWeight(hit.distance);
                        if (HitMat[i] == EFT.Ballistics.MaterialType.Glass) glassSum += RoomWeight(hit.distance);
                        closSum += RoomWeight(hit.distance);
                    }
                }
                else
                {
                    HitDist[i] = float.PositiveInfinity;
                    HitNormal[i] = Vector3.zero;
                    HitPoint[i] = Vector3.zero;
                    HitCollider[i] = null;
                }
                float elev = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (elev >= -10f) { all += w; allN++; }
                if (Mathf.Abs(elev) <= 30f) { hor += w; horN++; }
                if (elev > 45f) { up += w; upN++; }
                if (Mathf.Abs(elev) <= 30f) { hc += clos; hcN++; }
                if (elev > 30f) { oc += clos; ocN++; }
            }
            HorizClosure = hcN > 0 ? hc / hcN : float.NaN;
            OverClosure = ocN > 0 ? oc / ocN : float.NaN;
            float z = 0f;
            for (int k = 0; k < ZenithDirs.Length; k++)
                if (CountedRay.Raycast(o, ZenithDirs[k], out RaycastHit zh, MaxRange, mask, QueryTriggerInteraction.Ignore))
                    z += RoomWeight(zh.distance) * Surfaces.Closure(Surfaces.Material(zh.collider));
            ZenithClosure = z / ZenithDirs.Length;
            RoofClosure = Mathf.Min(OverClosure, ZenithClosure);
            Room = HorizClosure * RoofClosure;
            ThinShare = closSum > 0f ? thinSum / closSum : 0f;
            HardShare = closSum > 0f ? hardSum / closSum : 0f;
            GlassShare = closSum > 0f ? glassSum / closSum : 0f;
            HardRoom = Mathf.InverseLerp(HardRoomFrom, HardRoomFull, Room) * HardShare;
            Enclosure = allN > 0 ? all / allN : float.NaN;
            Horizontal = horN > 0 ? hor / horN : float.NaN;
            Overhead = upN > 0 ? up / upN : float.NaN;
            MeanHit = hitN > 0 ? hitSum / hitN : float.NaN;
            SizeFactor = SizeFor(MeanHit, HardRoom);
            LastSweepTime = Time.time;
            Valid = true;
        }

        // The room measure taken at another point: the muzzle (0.3.3). Raid 9: standing on a glass office's threshold, head
        // outside and muzzle inside sounded outdoor; head inside and muzzle outside sounded indoor, because everything was
        // measured at the head. The muzzle is what fills a room with sound, so fix 3c now measures there too and mixes the
        // two (setting "3g Muzzle weight"). Same rays and weights as the sweep (32 + 5), without storing per-ray hits;
        // reused while the muzzle stays within 0.15 m for 0.1 s, so full-auto doesn't re-cast every round.
        public static float MuzzleRoom = float.NaN, MuzzleRoof = float.NaN, MuzzleSize = float.NaN;
        private static Vector3 _muzzleAt = new Vector3(1e9f, 0, 0);
        private static float _muzzleTime = -100f;

        public static bool MeasureRoomAt(Vector3 o, int mask) => MeasureRoomAt(o, mask, Vector3.zero);

        // 0.6.11: aim -> "3k Muzzle lobe" weighting (see AcousticProbe.MeasureAt); MuzzleRoomSphere = the unweighted value.
        public static float MuzzleRoomSphere = float.NaN;
        private static Vector3 _muzzleAim;

        public static bool MeasureRoomAt(Vector3 o, int mask, Vector3 aim)
        {
            Diagnostics.Perf.Begin();
            try { return MeasureRoomAtCore(o, mask, aim); }
            finally { Diagnostics.Perf.End(Diagnostics.Perf.Part.MuzzleRoom); }
        }

        private static bool MeasureRoomAtCore(Vector3 o, int mask, Vector3 aim)
        {
            if (aim.sqrMagnitude > 1e-6f) aim.Normalize();
            if ((o - _muzzleAt).sqrMagnitude < 0.0225f && Time.time - _muzzleTime < 0.1f && Vector3.Dot(aim, _muzzleAim) > 0.995f) return !float.IsNaN(MuzzleRoom);
            _muzzleAt = o; _muzzleTime = Time.time; _muzzleAim = aim;
            float lk = Mathf.Clamp(Plugin.MuzzleLobe.Value, 0f, 2f);
            float hc = 0f, hcN = 0f, oc = 0f, ocN = 0f, hitSum = 0f, hitN = 0f, closSum = 0f, hardSum = 0f;
            float hcS = 0f, hcNS = 0f, ocS = 0f, ocNS = 0f;
            for (int i = 0; i < RayCount; i++)
            {
                Vector3 d = UnitDirs[i];
                float lw = AcousticProbe.LobeWeight(d, aim, lk);
                float clos = 0f;
                if (CountedRay.Raycast(o, d, out RaycastHit hit, MaxRange, mask, QueryTriggerInteraction.Ignore) && !IsGround(hit.normal))
                {
                    EFT.Ballistics.MaterialType m = Surfaces.Material(hit.collider);
                    float rw = RoomWeight(hit.distance);
                    clos = rw * Surfaces.Closure(m);
                    closSum += rw * lw;
                    if (Surfaces.IsHard(m)) hardSum += rw * lw;
                    hitSum += hit.distance * lw; hitN += lw;
                }
                float elev = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (Mathf.Abs(elev) <= 30f) { hc += clos * lw; hcN += lw; hcS += clos; hcNS++; }
                if (elev > 30f) { oc += clos * lw; ocN += lw; ocS += clos; ocNS++; }
            }
            float z = 0f, zW = 0f, zS = 0f;
            for (int k = 0; k < ZenithDirs.Length; k++)
            {
                float lw = AcousticProbe.LobeWeight(ZenithDirs[k], aim, lk);
                zW += lw;
                if (CountedRay.Raycast(o, ZenithDirs[k], out RaycastHit zh, MaxRange, mask, QueryTriggerInteraction.Ignore))
                {
                    float c = RoomWeight(zh.distance) * Surfaces.Closure(Surfaces.Material(zh.collider));
                    z += c * lw; zS += c;
                }
            }
            float horiz = hcN > 1e-4f ? hc / hcN : 0f, over = ocN > 1e-4f ? oc / ocN : 0f;
            MuzzleRoof = Mathf.Min(over, zW > 1e-4f ? z / zW : 0f);
            MuzzleRoom = horiz * MuzzleRoof;
            float horizS = hcNS > 0 ? hcS / hcNS : 0f, overS = ocNS > 0 ? ocS / ocNS : 0f;
            MuzzleRoomSphere = horizS * Mathf.Min(overS, zS / ZenithDirs.Length);
            float mean = hitN > 1e-4f ? hitSum / hitN : float.NaN;
            MuzzleHard = closSum > 0f ? hardSum / closSum : 0f;
            MuzzleSize = SizeFor(mean, Mathf.InverseLerp(HardRoomFrom, HardRoomFull, MuzzleRoom) * MuzzleHard);
            return true;
        }

        // How closed the direction you're aiming in is (0 open .. 1 a wall right in front), and how far the nearest surface
        // in that cone is (ground hits count as open). Called when you fire; 9 rays, reused within the same frame.
        public static bool MeasureAim(Vector3 origin, Vector3 forward, int mask, out float aimEnclosure, out float aimDistance)
        {
            Diagnostics.Perf.Begin();
            try { return MeasureAimCore(origin, forward, mask, out aimEnclosure, out aimDistance); }
            finally { Diagnostics.Perf.End(Diagnostics.Perf.Part.AimCone); }
        }

        private static bool MeasureAimCore(Vector3 origin, Vector3 forward, int mask, out float aimEnclosure, out float aimDistance)
        {
            aimEnclosure = float.NaN; aimDistance = float.NaN;
            if (forward.sqrMagnitude < 1e-6f) return false;
            forward.Normalize();
            if (_aimFrame == Time.frameCount && (origin - _aimOrigin).sqrMagnitude < 1e-4f && Vector3.Dot(forward, _aimForward) > 0.9999f)
            {
                aimEnclosure = _aimEnc; aimDistance = _aimDist;
                return true;
            }
            float closAim = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            Vector3 upv = Vector3.Cross(forward, right);
            float sum = 0f, nearest = float.PositiveInfinity;
            for (int i = 0; i < AimCone; i++)
            {
                Vector3 d = forward;
                if (i > 0)
                {
                    float a = (i - 1) * (360f / (AimCone - 1)) * Mathf.Deg2Rad;
                    float off = Mathf.Tan(AimHalfAngle * Mathf.Deg2Rad);
                    d = (forward + (right * Mathf.Cos(a) + upv * Mathf.Sin(a)) * off).normalized;
                }
                AimDirs[i] = d;
                if (CountedRay.Raycast(origin, d, out RaycastHit hit, MaxRange, mask, QueryTriggerInteraction.Ignore))
                {
                    AimHitDist[i] = hit.distance; AimHitNormal[i] = hit.normal; AimHitPoint[i] = hit.point; AimHitCollider[i] = hit.collider;
                    if (!IsGround(hit.normal))
                    {
                        closAim += RoomWeight(hit.distance) * Surfaces.Closure(Surfaces.Material(hit.collider));
                        sum += Weight(hit.distance);
                        if (hit.distance < nearest) nearest = hit.distance;
                    }
                }
                else
                {
                    AimHitDist[i] = float.PositiveInfinity; AimHitNormal[i] = Vector3.zero; AimHitPoint[i] = Vector3.zero; AimHitCollider[i] = null;
                }
            }
            aimEnclosure = sum / AimCone;
            aimDistance = nearest;
            AimClosure = closAim / AimCone;
            _aimFrame = Time.frameCount; _aimOrigin = origin; _aimForward = forward; _aimEnc = aimEnclosure; _aimDist = aimDistance;
            return true;
        }

        public static int CurrentMask()
        {
            if (!MonoBehaviourSingleton<SpatialAudioSystem>.Instantiated) return Physics.DefaultRaycastLayers;
            var s = MonoBehaviourSingleton<SpatialAudioSystem>.Instance.OcclusionSettings;
            return s != null ? s.commonSettings.occlusionLayerMask.value : Physics.DefaultRaycastLayers;
        }

        // Size factor (0.3.1): 0.5 at walls ~2 m away .. 1 from ~6 m: a small furnished space has a short tail, so less of
        // the long indoor recording. 0.3.3: not for hard closed rooms - a bare concrete stairwell rings (raid 9, Nakatani
        // stairs: walls 0.81-0.94, roof 1, all concrete/thick metal, but cut to ~0.55 indoor). size = lerp(size, 1, HardRoom).
        private static float SizeFor(float meanHit, float hardRoom)
        {
            float s = float.IsNaN(meanHit) ? 1f : Mathf.Lerp(SizeSmallFactor, 1f, Mathf.InverseLerp(SizeSmallM, SizeFullM, meanHit));
            return Mathf.Lerp(s, 1f, float.IsNaN(hardRoom) ? 0f : Mathf.Clamp01(hardRoom));
        }

        private static float RoomWeight(float distance) =>
            distance <= RoomFull ? 1f : Mathf.Clamp01(1f - (distance - RoomFull) / (MaxRange - RoomFull));

        private static float Weight(float distance) =>
            distance <= NearFull ? 1f : Mathf.Clamp01(1f - (distance - NearFull) / (MaxRange - NearFull));

        private static Vector3[] BuildZenith()
        {
            var v = new Vector3[5];
            v[0] = Vector3.up;
            float t = ZenithTiltDeg * Mathf.Deg2Rad;
            for (int i = 0; i < 4; i++)
            {
                float az = i * Mathf.PI * 0.5f;
                v[i + 1] = new Vector3(Mathf.Sin(t) * Mathf.Cos(az), Mathf.Cos(t), Mathf.Sin(t) * Mathf.Sin(az));
            }
            return v;
        }

        private static Vector3[] BuildFibonacciSphere(int n)
        {
            var v = new Vector3[n];
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < n; i++)
            {
                float y = 1f - (i + 0.5f) * 2f / n;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float th = golden * i;
                v[i] = new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r);
            }
            return v;
        }
    }
}
