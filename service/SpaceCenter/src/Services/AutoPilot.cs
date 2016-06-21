using System;
using System.Collections.Generic;
using System.Linq;
using KRPC.Continuations;
using KRPC.Server;
using KRPC.Service.Attributes;
using KRPC.SpaceCenter.AutoPilot;
using KRPC.SpaceCenter.ExtensionMethods;
using KRPC.Utils;
using UnityEngine;
using Tuple3 = KRPC.Utils.Tuple<double, double, double>;

namespace KRPC.SpaceCenter.Services
{
    /// <summary>
    /// Provides basic auto-piloting utilities for a vessel.
    /// Created by calling <see cref="Vessel.AutoPilot"/>.
    /// </summary>
    /// <remarks>
    /// If a client engages the auto-pilot and then closes its connection to the server,
    /// the auto-pilot will be disengaged and its target reference frame, direction and roll reset to default.
    /// </remarks>
    [KRPCClass (Service = "SpaceCenter")]
    public sealed class AutoPilot : Equatable<AutoPilot>
    {
        static readonly IDictionary<Guid, AutoPilot> engaged = new Dictionary<Guid, AutoPilot> ();
        readonly Guid vesselId;
        readonly AttitudeController attitudeController;
        IClient requestingClient;

        internal AutoPilot (global::Vessel vessel)
        {
            if (!engaged.ContainsKey (vessel.id))
                engaged [vessel.id] = null;
            vesselId = vessel.id;
            attitudeController = new AttitudeController (vessel);
        }

        /// <summary>
        /// Check the auto-pilots are for the same vessel.
        /// </summary>
        public override bool Equals (AutoPilot obj)
        {
            return vesselId == obj.vesselId;
        }

        /// <summary>
        /// Hash the auto-pilot.
        /// </summary>
        public override int GetHashCode ()
        {
            return vesselId.GetHashCode ();
        }

        /// <summary>
        /// The KSP vessel.
        /// </summary>
        public global::Vessel InternalVessel {
            get { return FlightGlobalsExtensions.GetVesselById (vesselId); }
        }

        /// <summary>
        /// Engage the auto-pilot.
        /// </summary>
        [KRPCMethod]
        public void Engage ()
        {
            requestingClient = KRPCCore.Context.RPCClient;
            engaged [vesselId] = this;
            attitudeController.Start ();
        }

        /// <summary>
        /// Disengage the auto-pilot.
        /// </summary>
        [KRPCMethod]
        public void Disengage ()
        {
            requestingClient = null;
            engaged [vesselId] = null;
        }

        /// <summary>
        /// Blocks until the vessel is pointing in the target direction and has the target roll (if set).
        /// </summary>
        [KRPCMethod]
        public void Wait ()
        {
            if (Error > 0.5f || RollError > 0.5f || InternalVessel.GetComponent<Rigidbody> ().angularVelocity.magnitude > 0.05f)
                throw new YieldException (new ParameterizedContinuationVoid (Wait));
        }

        /// <summary>
        /// The error, in degrees, between the direction the ship has been asked
        /// to point in and the direction it is pointing in. Returns zero if the auto-pilot
        /// has not been engaged and SAS is not enabled or is in stability assist mode.
        /// </summary>
        [KRPCProperty]
        public float Error {
            get {
                if (engaged [vesselId] == this)
                    return Vector3.Angle (InternalVessel.ReferenceTransform.up, ReferenceFrame.DirectionToWorldSpace (attitudeController.TargetDirection));
                else if (engaged [vesselId] != this && SAS && SASMode != SASMode.StabilityAssist)
                    return Vector3.Angle (InternalVessel.ReferenceTransform.up, SASTargetDirection ());
                else
                    return 0f;
            }
        }

        /// <summary>
        /// The error, in degrees, between the vessels current and target pitch.
        /// Returns zero if the auto-pilot has not been engaged.
        /// </summary>
        [KRPCProperty]
        public float PitchError {
            get {
                if (engaged [vesselId] != this)
                    return 0f;
                var currentPitch = ReferenceFrame.RotationFromWorldSpace (InternalVessel.ReferenceTransform.rotation).PitchHeadingRoll ().x;
                return (float)Math.Abs (GeometryExtensions.ClampAngle180 (attitudeController.TargetPitch - currentPitch));
            }
        }

        /// <summary>
        /// The error, in degrees, between the vessels current and target heading.
        /// Returns zero if the auto-pilot has not been engaged.
        /// </summary>
        [KRPCProperty]
        public float HeadingError {
            get {
                if (engaged [vesselId] != this)
                    return 0f;
                var currentHeading = ReferenceFrame.RotationFromWorldSpace (InternalVessel.ReferenceTransform.rotation).PitchHeadingRoll ().y;
                return (float)Math.Abs (GeometryExtensions.ClampAngle180 (attitudeController.TargetHeading - currentHeading));
            }
        }

        /// <summary>
        /// The error, in degrees, between the vessels current and target roll.
        /// Returns zero if the auto-pilot has not been engaged or no target roll is set.
        /// </summary>
        [KRPCProperty]
        public float RollError {
            get {
                if (engaged [vesselId] != this || double.IsNaN (attitudeController.TargetRoll))
                    return 0f;
                var currentRoll = ReferenceFrame.RotationFromWorldSpace (InternalVessel.ReferenceTransform.rotation).PitchHeadingRoll ().z;
                return (float)Math.Abs (GeometryExtensions.ClampAngle180 (attitudeController.TargetRoll - currentRoll));
            }
        }

        /// <summary>
        /// The reference frame for the target direction (<see cref="AutoPilot.TargetDirection"/>).
        /// </summary>
        [KRPCProperty]
        public ReferenceFrame ReferenceFrame {
            get { return attitudeController.ReferenceFrame; }
            set { attitudeController.ReferenceFrame = value; }
        }

        /// <summary>
        /// The target pitch, in degrees, between -90° and +90°.
        /// </summary>
        [KRPCProperty]
        public float TargetPitch {
            get { return (float)attitudeController.TargetPitch; }
            set { attitudeController.TargetPitch = value; }
        }

        /// <summary>
        /// The target heading, in degrees, between 0° and 360°.
        /// </summary>
        [KRPCProperty]
        public float TargetHeading {
            get { return (float)attitudeController.TargetHeading; }
            set { attitudeController.TargetHeading = value; }
        }

        /// <summary>
        /// The target roll, in degrees. <c>NaN</c> if no target roll is set.
        /// </summary>
        [KRPCProperty]
        public float TargetRoll {
            get { return (float)attitudeController.TargetRoll; }
            set { attitudeController.TargetRoll = value; }
        }

        /// <summary>
        /// Set target pitch and heading angles.
        /// </summary>
        /// <param name="pitch">Target pitch angle, in degrees between -90° and +90°.</param>
        /// <param name="heading">Target heading angle, in degrees between 0° and 360°.</param>
        //TODO: deprecate this in favour of TargetPitch and TargetHeading properties?
        [KRPCMethod]
        public void TargetPitchAndHeading (float pitch, float heading)
        {
            attitudeController.TargetPitch = pitch;
            attitudeController.TargetHeading = heading;
        }

        /// <summary>
        /// Direction vector corresponding to the target pitch and heading.
        /// </summary>
        [KRPCProperty]
        public Tuple3 TargetDirection {
            get { return attitudeController.TargetDirection.ToTuple (); }
            set {
                //FIXME: QuaternionD.FromToRotation method not available at runtime
                var rotation = (QuaternionD)Quaternion.FromToRotation (Vector3d.up, value.ToVector ());
                var phr = rotation.PitchHeadingRoll ();
                attitudeController.TargetPitch = phr.x;
                attitudeController.TargetHeading = phr.y;
            }
        }

        /// <summary>
        /// The state of SAS.
        /// </summary>
        /// <remarks>Equivalent to <see cref="Control.SAS"/></remarks>
        [KRPCProperty]
        public bool SAS {
            get { return InternalVessel.ActionGroups.groups [BaseAction.GetGroupIndex (KSPActionGroup.SAS)]; }
            set { InternalVessel.ActionGroups.SetGroup (KSPActionGroup.SAS, value); }
        }

        /// <summary>
        /// The current <see cref="SASMode"/>.
        /// These modes are equivalent to the mode buttons to the left of the navball that appear when SAS is enabled.
        /// </summary>
        /// <remarks>Equivalent to <see cref="Control.SASMode"/></remarks>
        [KRPCProperty]
        public SASMode SASMode {
            get { return Control.GetSASMode (InternalVessel); }
            set { Control.SetSASMode (InternalVessel, value); }
        }

        /// <summary>
        /// The threshold at which the autopilot will try to match the target roll angle, if any.
        /// Defaults to 5 degrees.
        /// </summary>
        [KRPCProperty]
        public double RollThreshold {
            get { return attitudeController.RollThreshold; }
            set { attitudeController.RollThreshold = value; }
        }

        /// <summary>
        /// The maximum amount of time that the vessel should need to come to a complete stop.
        /// This determines the maximum angular velocity of the vessel.
        /// A vector of three stopping times, in seconds, one for each of the pitch, roll and yaw axes.
        /// Defaults to 0.5 seconds for each axis.
        /// </summary>
        [KRPCProperty]
        public Tuple3 StoppingTime {
            get { return attitudeController.StoppingTime.ToTuple (); }
            set { attitudeController.StoppingTime = value.ToVector (); }
        }

        /// <summary>
        /// The time the vessel should take to come to a stop pointing in the target direction.
        /// This determines the angular acceleration used to decelerate the vessel.
        /// A vector of three times, in seconds, one for each of the pitch, roll and yaw axes.
        /// Defaults to 5 seconds for each axis.
        /// </summary>
        [KRPCProperty]
        public Tuple3 DecelerationTime {
            get { return attitudeController.DecelerationTime.ToTuple (); }
            set { attitudeController.DecelerationTime = value.ToVector (); }
        }

        /// <summary>
        /// The angle at which the autopilot considers the vessel to be pointing close to the target.
        /// This determines the midpoint of the target velocity attenuation function.
        /// A vector of three angles, in degrees, one for each of the pitch, roll and yaw axes.
        /// Defaults to 1° for each axis.
        /// </summary>
        [KRPCProperty]
        public Tuple3 AttenuationAngle {
            get { return attitudeController.AttenuationAngle.ToTuple (); }
            set { attitudeController.AttenuationAngle = value.ToVector (); }
        }

        /// <summary>
        /// Whether the rotation rate controllers PID parameters should be automatically tuned using the
        /// vessels moment of inertia and available torque. Defaults to <c>true</c>.
        /// See <see cref="TimeToPeak"/> and  <see cref="Overshoot"/>.
        /// </summary>
        [KRPCProperty]
        public bool AutoTune {
            get { return attitudeController.AutoTune; }
            set { attitudeController.AutoTune = value; }
        }

        /// <summary>
        /// The target time to peak used to autotune the PID controllers.
        /// A vector of three times, in seconds, for each of the pitch, roll and yaw axes.
        /// Defaults to 3 seconds for each axis.
        /// </summary>
        [KRPCProperty]
        public Tuple3 TimeToPeak {
            get { return attitudeController.TimeToPeak.ToTuple (); }
            set { attitudeController.TimeToPeak = value.ToVector (); }
        }

        /// <summary>
        /// The target overshoot percentage used to autotune the PID controllers.
        /// A vector of three values, between 0 and 1, for each of the pitch, roll and yaw axes.
        /// Defaults to 0.01 for each axis.
        /// </summary>
        [KRPCProperty]
        public Tuple3 Overshoot {
            get { return attitudeController.Overshoot.ToTuple (); }
            set { attitudeController.Overshoot = value.ToVector (); }
        }

        /// <summary>
        /// Gains for the pitch PID controller.
        /// </summary>
        /// <remarks>
        /// When <see cref="AutoTune"/> is true, these values are updated automatically, which will overwrite any manual changes.
        /// </remarks>
        [KRPCProperty]
        public Tuple3 PitchPIDGains {
            get {
                var pid = attitudeController.PitchPID;
                return new Tuple3 (pid.Kp, pid.Ki, pid.Kd);
            }
            set { attitudeController.PitchPID.SetParameters (value.Item1, value.Item2, value.Item3); }
        }

        /// <summary>
        /// Gains for the roll PID controller.
        /// </summary>
        /// <remarks>
        /// When <see cref="AutoTune"/> is true, these values are updated automatically, which will overwrite any manual changes.
        /// </remarks>
        [KRPCProperty]
        public Tuple3 RollPIDGains {
            get {
                var pid = attitudeController.RollPID;
                return new Tuple3 (pid.Kp, pid.Ki, pid.Kd);
            }
            set { attitudeController.RollPID.SetParameters (value.Item1, value.Item2, value.Item3); }
        }

        /// <summary>
        /// Gains for the yaw PID controller.
        /// </summary>
        /// <remarks>
        /// When <see cref="AutoTune"/> is true, these values are updated automatically, which will overwrite any manual changes.
        /// </remarks>
        [KRPCProperty]
        public Tuple3 YawPIDGains {
            get {
                var pid = attitudeController.YawPID;
                return new Tuple3 (pid.Kp, pid.Ki, pid.Kd);
            }
            set { attitudeController.YawPID.SetParameters (value.Item1, value.Item2, value.Item3); }
        }

        /// <summary>
        /// The direction vector that the SAS autopilot is trying to hold in world space
        /// </summary>
        Vector3d SASTargetDirection ()
        {
            // Stability assist
            if (SASMode == SASMode.StabilityAssist)
                throw new InvalidOperationException ("No target direction in stability assist mode");

            // Maneuver node
            if (SASMode == SASMode.Maneuver) {
                var node = InternalVessel.patchedConicSolver.maneuverNodes.OrderBy (x => x.UT).FirstOrDefault ();
                if (node == null)
                    throw new InvalidOperationException ("No maneuver node");
                return new Node (InternalVessel, node).WorldBurnVector;
            }

            // Orbital directions, in different speed modes
            if (SASMode == SASMode.Prograde || SASMode == SASMode.Retrograde ||
                SASMode == SASMode.Normal || SASMode == SASMode.AntiNormal ||
                SASMode == SASMode.Radial || SASMode == SASMode.AntiRadial) {

                if (Control.GetSpeedMode () == SpeedMode.Orbit) {
                    switch (SASMode) {
                    case SASMode.Prograde:
                        return ReferenceFrame.Orbital (InternalVessel).DirectionToWorldSpace (Vector3d.up);
                    case SASMode.Retrograde:
                        return ReferenceFrame.Orbital (InternalVessel).DirectionToWorldSpace (Vector3d.down);
                    case SASMode.Normal:
                        return ReferenceFrame.Orbital (InternalVessel).DirectionToWorldSpace (Vector3d.forward);
                    case SASMode.AntiNormal:
                        return ReferenceFrame.Orbital (InternalVessel).DirectionToWorldSpace (Vector3d.back);
                    case SASMode.Radial:
                        return ReferenceFrame.Orbital (InternalVessel).DirectionToWorldSpace (Vector3d.left);
                    case SASMode.AntiRadial:
                        return ReferenceFrame.Orbital (InternalVessel).DirectionToWorldSpace (Vector3d.right);
                    }
                } else if (Control.GetSpeedMode () == SpeedMode.Surface) {
                    switch (SASMode) {
                    case SASMode.Prograde:
                        return ReferenceFrame.SurfaceVelocity (InternalVessel).DirectionToWorldSpace (Vector3d.up);
                    case SASMode.Retrograde:
                        return ReferenceFrame.SurfaceVelocity (InternalVessel).DirectionToWorldSpace (Vector3d.down);
                    case SASMode.Normal:
                        return ReferenceFrame.Object (InternalVessel.orbit.referenceBody).DirectionToWorldSpace (Vector3d.up);
                    case SASMode.AntiNormal:
                        return ReferenceFrame.Object (InternalVessel.orbit.referenceBody).DirectionToWorldSpace (Vector3d.down);
                    case SASMode.Radial:
                        return ReferenceFrame.Surface (InternalVessel).DirectionToWorldSpace (Vector3d.right);
                    case SASMode.AntiRadial:
                        return ReferenceFrame.Surface (InternalVessel).DirectionToWorldSpace (Vector3d.left);
                    }
                } else if (Control.GetSpeedMode () == SpeedMode.Target) {
                    switch (SASMode) {
                    case SASMode.Prograde:
                        return InternalVessel.GetWorldVelocity () - FlightGlobals.fetch.VesselTarget.GetWorldVelocity ();
                    case SASMode.Retrograde:
                        return FlightGlobals.fetch.VesselTarget.GetWorldVelocity () - InternalVessel.GetWorldVelocity ();
                    case SASMode.Normal:
                        return ReferenceFrame.Object (InternalVessel.orbit.referenceBody).DirectionToWorldSpace (Vector3d.up);
                    case SASMode.AntiNormal:
                        return ReferenceFrame.Object (InternalVessel.orbit.referenceBody).DirectionToWorldSpace (Vector3d.down);
                    case SASMode.Radial:
                        return ReferenceFrame.Surface (InternalVessel).DirectionToWorldSpace (Vector3d.right);
                    case SASMode.AntiRadial:
                        return ReferenceFrame.Surface (InternalVessel).DirectionToWorldSpace (Vector3d.left);
                    }
                }
                throw new InvalidOperationException ("Unknown speed mode for orbital direction");
            }

            // Target and anti-target
            if (SASMode == SASMode.Target || SASMode == SASMode.AntiTarget) {
                var target = FlightGlobals.fetch.VesselTarget;
                if (target == null)
                    throw new InvalidOperationException ("No target");
                var direction = target.GetWorldPosition () - InternalVessel.GetWorldPos3D ();
                if (SASMode == SASMode.AntiTarget)
                    direction *= -1;
                return direction;
            }

            throw new InvalidOperationException ("Unknown SAS mode");
        }

        internal static bool Fly (global::Vessel vessel, PilotAddon.ControlInputs state)
        {
            // Get the auto-pilot object. Do nothing if there is no auto-pilot engaged for this vessel.
            if (!engaged.ContainsKey (vessel.id))
                return false;
            var autoPilot = engaged [vessel.id];
            if (autoPilot == null)
                return false;
            // If the client that engaged the auto-pilot has disconnected, disengage and reset the auto-pilot
            if (autoPilot.requestingClient != null && !autoPilot.requestingClient.Connected) {
                autoPilot.attitudeController.ReferenceFrame = ReferenceFrame.Surface (vessel);
                autoPilot.attitudeController.TargetPitch = 0;
                autoPilot.attitudeController.TargetHeading = 0;
                autoPilot.attitudeController.TargetRoll = double.NaN;
                autoPilot.Disengage ();
                return false;
            }
            // Run the auto-pilot
            autoPilot.SAS = false;
            autoPilot.attitudeController.Update (state);
            return true;
        }
    }
}
