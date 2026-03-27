using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using ReVerso.Data;

/// <summary>
/// Mirror therapy manager that mirrors default XR hand skeletons (no custom rigs).
/// </summary>
public class MirrorTherapyHandTracking : MonoBehaviour
{
    [Serializable]
    public class CalibrationData
    {
        public bool isCalibrated;
        public Vector3 origin;
        public float tableHeight;
        public Vector3 mirrorNormal;
        public Vector3 forwardDirection;
    }

    private static readonly int JointCount = XRHandJointID.EndMarker.ToIndex();

    [Header("Configuration Therapie")]
    [Tooltip("Cote du corps a entrainer (cote affecte).")]
    [SerializeField] private CoteAffecte coteEntraine = CoteAffecte.Gauche;

    [Header("Tracking XR")]
    [SerializeField] private XRHandTrackingEvents leftHandTrackingEvents;
    [SerializeField] private XRHandTrackingEvents rightHandTrackingEvents;

    [Tooltip("Transform du XR Origin pour convertir tracking space vers world space.")]
    [SerializeField] private Transform xrOriginTransform;

    [Header("Skeletones mains par defaut")]
    [SerializeField] private XRHandSkeletonDriver leftSkeletonDriver;
    [SerializeField] private XRHandSkeletonDriver rightSkeletonDriver;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;

    private CalibrationData calibration = new CalibrationData();

    private bool leftHandTracked;
    private bool rightHandTracked;
    private bool isMirrorActive;

    private Pose[] leftJointPoses = new Pose[JointCount];
    private Pose[] rightJointPoses = new Pose[JointCount];
    private bool[] leftJointValid = new bool[JointCount];
    private bool[] rightJointValid = new bool[JointCount];

    public CalibrationData CurrentCalibration => calibration;
    public bool IsCalibrated => calibration.isCalibrated;

    public bool ValidHandIsTracked =>
        coteEntraine == CoteAffecte.Gauche ? rightHandTracked : leftHandTracked;

    public bool AffectedHandIsTracked =>
        coteEntraine == CoteAffecte.Gauche ? leftHandTracked : rightHandTracked;

    public bool BothHandsTracked => leftHandTracked && rightHandTracked;

    public CoteAffecte CoteEntraine
    {
        get => coteEntraine;
        set
        {
            coteEntraine = value;
            UpdateDriverStates();
        }
    }

    public bool MirrorActive
    {
        get => isMirrorActive;
        set
        {
            isMirrorActive = value;
            UpdateDriverStates();
        }
    }

    private void Awake()
    {
        AutoBindSkeletonDrivers();
    }

    private void OnEnable()
    {
        AutoBindSkeletonDrivers();
        SubscribeEvents();
        UpdateDriverStates();
        Debug.Log("[MirrorTherapy] Script actif (squelette mains par defaut).");
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
        isMirrorActive = false;
        SetDriverEnabled(leftSkeletonDriver, true);
        SetDriverEnabled(rightSkeletonDriver, true);
    }

    private void LateUpdate()
    {
        if (!isMirrorActive || !calibration.isCalibrated)
            return;

        bool validIsRight = coteEntraine == CoteAffecte.Gauche;
        bool validTracked = validIsRight ? rightHandTracked : leftHandTracked;
        if (!validTracked)
            return;

        Pose[] sourcePoses = validIsRight ? rightJointPoses : leftJointPoses;
        bool[] sourceValid = validIsRight ? rightJointValid : leftJointValid;
        XRHandSkeletonDriver affectedDriver = validIsRight ? leftSkeletonDriver : rightSkeletonDriver;

        ApplyMirroredToSkeleton(affectedDriver, sourcePoses, sourceValid);
    }

    [ContextMenu("Calibrer (les deux mains doivent etre trackees)")]
    public bool Calibrer()
    {
        int wristIdx = XRHandJointID.Wrist.ToIndex();

        if (!leftHandTracked || !rightHandTracked || !leftJointValid[wristIdx] || !rightJointValid[wristIdx])
        {
            Debug.LogWarning("[MirrorTherapy] Calibration impossible: les deux poignets doivent etre trackes.");
            return false;
        }

        Vector3 leftWrist = leftJointPoses[wristIdx].position;
        Vector3 rightWrist = rightJointPoses[wristIdx].position;

        calibration.origin = (leftWrist + rightWrist) * 0.5f;
        calibration.tableHeight = calibration.origin.y;

        Vector3 leftToRight = rightWrist - leftWrist;
        leftToRight.y = 0f;

        if (leftToRight.sqrMagnitude < 0.001f)
        {
            Debug.LogError("[MirrorTherapy] Calibration echouee: les deux poignets sont trop proches.");
            return false;
        }

        calibration.mirrorNormal = leftToRight.normalized;
        calibration.forwardDirection = Vector3.Cross(Vector3.up, calibration.mirrorNormal).normalized;
        calibration.isCalibrated = true;

        UpdateDriverStates();

        Debug.Log($"[MirrorTherapy] Calibration OK. Origine={calibration.origin} Hauteur={calibration.tableHeight:F3}m");
        return true;
    }

    [ContextMenu("Reset Calibration")]
    public void ResetCalibration()
    {
        calibration.isCalibrated = false;
        UpdateDriverStates();
        Debug.Log("[MirrorTherapy] Calibration reinitialisee.");
    }

    private void SubscribeEvents()
    {
        if (leftHandTrackingEvents != null)
        {
            leftHandTrackingEvents.jointsUpdated.AddListener(OnLeftJointsUpdated);
            leftHandTrackingEvents.trackingAcquired.AddListener(OnLeftTrackingAcquired);
            leftHandTrackingEvents.trackingLost.AddListener(OnLeftTrackingLost);
        }

        if (rightHandTrackingEvents != null)
        {
            rightHandTrackingEvents.jointsUpdated.AddListener(OnRightJointsUpdated);
            rightHandTrackingEvents.trackingAcquired.AddListener(OnRightTrackingAcquired);
            rightHandTrackingEvents.trackingLost.AddListener(OnRightTrackingLost);
        }
    }

    private void UnsubscribeEvents()
    {
        if (leftHandTrackingEvents != null)
        {
            leftHandTrackingEvents.jointsUpdated.RemoveListener(OnLeftJointsUpdated);
            leftHandTrackingEvents.trackingAcquired.RemoveListener(OnLeftTrackingAcquired);
            leftHandTrackingEvents.trackingLost.RemoveListener(OnLeftTrackingLost);
        }

        if (rightHandTrackingEvents != null)
        {
            rightHandTrackingEvents.jointsUpdated.RemoveListener(OnRightJointsUpdated);
            rightHandTrackingEvents.trackingAcquired.RemoveListener(OnRightTrackingAcquired);
            rightHandTrackingEvents.trackingLost.RemoveListener(OnRightTrackingLost);
        }
    }

    private void OnLeftTrackingAcquired()
    {
        leftHandTracked = true;
    }

    private void OnLeftTrackingLost()
    {
        leftHandTracked = false;
        Array.Clear(leftJointValid, 0, leftJointValid.Length);
    }

    private void OnRightTrackingAcquired()
    {
        rightHandTracked = true;
    }

    private void OnRightTrackingLost()
    {
        rightHandTracked = false;
        Array.Clear(rightJointValid, 0, rightJointValid.Length);
    }

    private void OnLeftJointsUpdated(XRHandJointsUpdatedEventArgs args)
    {
        CacheJointPoses(args.hand, leftJointPoses, leftJointValid);
    }

    private void OnRightJointsUpdated(XRHandJointsUpdatedEventArgs args)
    {
        CacheJointPoses(args.hand, rightJointPoses, rightJointValid);
    }

    private void CacheJointPoses(XRHand hand, Pose[] poses, bool[] valid)
    {
        for (int i = 0; i < JointCount; i++)
        {
            XRHandJointID jointId = XRHandJointIDUtility.FromIndex(i);
            XRHandJoint joint = hand.GetJoint(jointId);

            if (joint.TryGetPose(out Pose trackingPose))
            {
                poses[i] = TrackingToWorld(trackingPose);
                valid[i] = true;
            }
            else
            {
                valid[i] = false;
            }
        }
    }

    private Pose TrackingToWorld(Pose trackingPose)
    {
        if (xrOriginTransform == null)
            return trackingPose;

        return new Pose(
            xrOriginTransform.TransformPoint(trackingPose.position),
            xrOriginTransform.rotation * trackingPose.rotation
        );
    }

    private void UpdateDriverStates()
    {
        bool canMirror = isMirrorActive && calibration.isCalibrated;
        if (!canMirror)
        {
            SetDriverEnabled(leftSkeletonDriver, true);
            SetDriverEnabled(rightSkeletonDriver, true);
            return;
        }

        bool validIsRight = coteEntraine == CoteAffecte.Gauche;

        XRHandSkeletonDriver validDriver = validIsRight ? rightSkeletonDriver : leftSkeletonDriver;
        XRHandSkeletonDriver affectedDriver = validIsRight ? leftSkeletonDriver : rightSkeletonDriver;

        SetDriverEnabled(validDriver, true);
        SetDriverEnabled(affectedDriver, false);
    }

    private static void SetDriverEnabled(XRHandSkeletonDriver driver, bool enabled)
    {
        if (driver != null)
            driver.enabled = enabled;
    }

    private void AutoBindSkeletonDrivers()
    {
        if (leftHandTrackingEvents == null || rightHandTrackingEvents == null)
            return;

        if (leftSkeletonDriver != null && rightSkeletonDriver != null)
            return;

        XRHandSkeletonDriver[] drivers = FindObjectsByType<XRHandSkeletonDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var driver in drivers)
        {
            if (driver == null) continue;

            if (leftSkeletonDriver == null && driver.handTrackingEvents == leftHandTrackingEvents)
                leftSkeletonDriver = driver;

            if (rightSkeletonDriver == null && driver.handTrackingEvents == rightHandTrackingEvents)
                rightSkeletonDriver = driver;
        }

        if (leftSkeletonDriver == null || rightSkeletonDriver == null)
            Debug.LogWarning("[MirrorTherapy] Skeleton drivers non trouves automatiquement. Assignez-les dans l'inspecteur si besoin.");
    }

    private void ApplyMirroredToSkeleton(XRHandSkeletonDriver targetDriver, Pose[] sourcePoses, bool[] sourceValid)
    {
        if (targetDriver == null)
            return;

        List<JointToTransformReference> refs = targetDriver.jointTransformReferences;
        if (refs == null)
            return;

        for (int i = 0; i < refs.Count; i++)
        {
            JointToTransformReference jointRef = refs[i];
            Transform jointTransform = jointRef.jointTransform;
            int sourceIdx = jointRef.xrHandJointID.ToIndex();

            if (jointTransform == null)
                continue;
            if (sourceIdx < 0 || sourceIdx >= sourcePoses.Length)
                continue;
            if (!sourceValid[sourceIdx])
                continue;

            Vector3 mirroredPos = MirrorPosition(sourcePoses[sourceIdx].position);
            Quaternion mirroredRot = MirrorRotation(sourcePoses[sourceIdx].rotation);

            jointTransform.position = mirroredPos;
            jointTransform.rotation = mirroredRot;
        }
    }

    private Vector3 MirrorPosition(Vector3 worldPos)
    {
        Vector3 toPos = worldPos - calibration.origin;
        float dot = Vector3.Dot(toPos, calibration.mirrorNormal);
        return worldPos - 2f * dot * calibration.mirrorNormal;
    }

    private Quaternion MirrorRotation(Quaternion rot)
    {
        Vector3 normal = calibration.mirrorNormal;

        Vector3 fwd = Vector3.Reflect(rot * Vector3.forward, normal);
        Vector3 up = Vector3.Reflect(rot * Vector3.up, normal);

        if (fwd.sqrMagnitude < 0.0001f)
            return rot;

        return Quaternion.LookRotation(fwd, up);
    }

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || !calibration.isCalibrated)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(calibration.origin, 0.03f);

        Gizmos.color = Color.red;
        Gizmos.DrawRay(calibration.origin, calibration.mirrorNormal * 0.15f);

        Gizmos.color = Color.blue;
        Gizmos.DrawRay(calibration.origin, calibration.forwardDirection * 0.15f);
    }
}
