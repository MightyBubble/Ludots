using System;

namespace AnimationAcceptanceMod.Runtime
{
    internal enum AnimationAcceptanceDriverMode : byte
    {
        Auto = 0,
        Manual = 1,
    }

    internal sealed class AnimationAcceptanceRigControlSlot
    {
        public AnimationAcceptanceRigControlSlot(AnimationAcceptanceRigDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            DriverMode = AnimationAcceptanceDriverMode.Auto;
            OverlayWeight01 = 1f;
            ActiveProfileId = "auto";
            ApplyProfile(definition.Profiles.Length > 0 ? definition.Profiles[0] : new AnimationAcceptanceExampleProfile(
                "default",
                "Default",
                "Fallback acceptance profile.",
                0f,
                false,
                0f,
                0f,
                1f,
                0f,
                0f,
                false));
            DriverMode = AnimationAcceptanceDriverMode.Auto;
            ActiveProfileId = "auto";
        }

        public AnimationAcceptanceRigDefinition Definition { get; }
        public AnimationAcceptanceDriverMode DriverMode { get; set; }
        public string ActiveProfileId { get; set; }
        public float Speed { get; set; }
        public bool MoveEnabled { get; set; }
        public float FacingYawRad { get; set; }
        public float AimYawRad { get; set; }
        public float OverlayWeight01 { get; set; }
        public float LowerBodyPhase01 { get; set; }
        public float IdleOverlayClock01 { get; set; }
        public float OverlayNormalizedTime01 { get; set; }
        public float FireNormalizedTime01 { get; set; }
        public bool OverlayFiring { get; set; }
        public bool PendingFireTrigger { get; set; }

        public void ApplyProfile(AnimationAcceptanceExampleProfile profile)
        {
            Speed = Math.Clamp(profile.Speed, 0f, 1f);
            MoveEnabled = profile.MoveEnabled;
            FacingYawRad = profile.FacingYawRad;
            AimYawRad = profile.AimYawRad;
            OverlayWeight01 = Math.Clamp(profile.OverlayWeight01, 0f, 1f);
            LowerBodyPhase01 = Wrap01(profile.LowerBodyPhase01);
            IdleOverlayClock01 = Wrap01(profile.OverlayNormalizedTime01);
            OverlayNormalizedTime01 = Wrap01(profile.OverlayNormalizedTime01);
            OverlayFiring = false;
            FireNormalizedTime01 = 0f;
            PendingFireTrigger = false;
            ActiveProfileId = profile.Id;
            if (profile.FireImmediately)
            {
                QueueFire();
            }
        }

        public void QueueFire()
        {
            PendingFireTrigger = true;
            OverlayFiring = true;
            FireNormalizedTime01 = 0f;
            OverlayNormalizedTime01 = 0f;
        }

        public void MarkCustom()
        {
            ActiveProfileId = "custom";
        }

        public static float Wrap01(float value)
        {
            float wrapped = value - MathF.Floor(value);
            return wrapped < 0f ? wrapped + 1f : wrapped;
        }
    }

    internal sealed class AnimationAcceptanceControlState
    {
        public AnimationAcceptanceControlState()
        {
            Tank = new AnimationAcceptanceRigControlSlot(AnimationAcceptanceRigCatalog.Tank);
            Humanoid = new AnimationAcceptanceRigControlSlot(AnimationAcceptanceRigCatalog.Humanoid);
            PlaybackScale = 1f;
            SelectedRig = AnimationAcceptanceRigId.Tank;
        }

        public float PlaybackScale { get; private set; }
        public AnimationAcceptanceRigId SelectedRig { get; private set; }
        public AnimationAcceptanceRigControlSlot Tank { get; }
        public AnimationAcceptanceRigControlSlot Humanoid { get; }

        public AnimationAcceptanceRigControlSlot GetSlot(AnimationAcceptanceRigId rigId)
        {
            return rigId switch
            {
                AnimationAcceptanceRigId.Tank => Tank,
                AnimationAcceptanceRigId.Humanoid => Humanoid,
                _ => throw new ArgumentOutOfRangeException(nameof(rigId)),
            };
        }

        public void SetSelectedRig(AnimationAcceptanceRigId rigId)
        {
            SelectedRig = rigId;
        }

        public void SetPlaybackScale(float scale)
        {
            PlaybackScale = Math.Clamp(scale, 0.25f, 3f);
        }

        public void SetDriverMode(AnimationAcceptanceRigId rigId, AnimationAcceptanceDriverMode mode)
        {
            var slot = GetSlot(rigId);
            slot.DriverMode = mode;
            if (mode == AnimationAcceptanceDriverMode.Auto)
            {
                slot.ActiveProfileId = "auto";
            }
            else if (string.Equals(slot.ActiveProfileId, "auto", StringComparison.Ordinal))
            {
                slot.MarkCustom();
            }

            SelectedRig = rigId;
        }

        public void ApplyProfile(AnimationAcceptanceRigId rigId, string profileId)
        {
            var slot = GetSlot(rigId);
            var definition = slot.Definition;
            for (int i = 0; i < definition.Profiles.Length; i++)
            {
                if (!string.Equals(definition.Profiles[i].Id, profileId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                slot.ApplyProfile(definition.Profiles[i]);
                slot.DriverMode = AnimationAcceptanceDriverMode.Manual;
                SelectedRig = rigId;
                return;
            }
        }

        public void StepSpeed(AnimationAcceptanceRigId rigId, float delta)
        {
            var slot = GetSlot(rigId);
            slot.Speed = Math.Clamp(slot.Speed + delta, 0f, 1f);
            if (slot.Speed <= 0.01f)
            {
                slot.MoveEnabled = false;
            }
            else if (delta > 0f)
            {
                slot.MoveEnabled = true;
            }

            slot.MarkCustom();
            slot.DriverMode = AnimationAcceptanceDriverMode.Manual;
            SelectedRig = rigId;
        }

        public void ToggleMove(AnimationAcceptanceRigId rigId)
        {
            var slot = GetSlot(rigId);
            slot.MoveEnabled = !slot.MoveEnabled;
            if (!slot.MoveEnabled && slot.Speed < 0.05f)
            {
                slot.Speed = 0f;
            }
            else if (slot.MoveEnabled && slot.Speed < 0.2f)
            {
                slot.Speed = 0.2f;
            }

            slot.MarkCustom();
            slot.DriverMode = AnimationAcceptanceDriverMode.Manual;
            SelectedRig = rigId;
        }

        public void StepFacing(AnimationAcceptanceRigId rigId, float deltaRad)
        {
            var slot = GetSlot(rigId);
            slot.FacingYawRad += deltaRad;
            slot.MarkCustom();
            slot.DriverMode = AnimationAcceptanceDriverMode.Manual;
            SelectedRig = rigId;
        }

        public void SetFacing(AnimationAcceptanceRigId rigId, float yawRad)
        {
            var slot = GetSlot(rigId);
            slot.FacingYawRad = yawRad;
            slot.MarkCustom();
            slot.DriverMode = AnimationAcceptanceDriverMode.Manual;
            SelectedRig = rigId;
        }

        public void StepAim(AnimationAcceptanceRigId rigId, float deltaRad)
        {
            var slot = GetSlot(rigId);
            slot.AimYawRad = Math.Clamp(slot.AimYawRad + deltaRad, -1.35f, 1.35f);
            slot.MarkCustom();
            slot.DriverMode = AnimationAcceptanceDriverMode.Manual;
            SelectedRig = rigId;
        }

        public void SetAim(AnimationAcceptanceRigId rigId, float yawRad)
        {
            var slot = GetSlot(rigId);
            slot.AimYawRad = Math.Clamp(yawRad, -1.35f, 1.35f);
            slot.MarkCustom();
            slot.DriverMode = AnimationAcceptanceDriverMode.Manual;
            SelectedRig = rigId;
        }

        public void StepOverlayWeight(AnimationAcceptanceRigId rigId, float delta)
        {
            var slot = GetSlot(rigId);
            slot.OverlayWeight01 = Math.Clamp(slot.OverlayWeight01 + delta, 0f, 1f);
            slot.MarkCustom();
            slot.DriverMode = AnimationAcceptanceDriverMode.Manual;
            SelectedRig = rigId;
        }

        public void TriggerFire(AnimationAcceptanceRigId rigId)
        {
            var slot = GetSlot(rigId);
            slot.QueueFire();
            slot.MarkCustom();
            slot.DriverMode = AnimationAcceptanceDriverMode.Manual;
            SelectedRig = rigId;
        }
    }
}
