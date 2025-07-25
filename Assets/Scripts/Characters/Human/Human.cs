using ApplicationManagers;
using Cameras;
using Controllers;
using CustomLogic;
using CustomSkins;
using Effects;
using GameManagers;
using GameProgress;
using Map;
using Photon.Pun;
using Photon.Realtime;
using Settings;
using SimpleJSONFixed;
using System;
using System.Collections;
using System.Collections.Generic;
using UI;
using UnityEngine;
using Utility;
using Weather;

namespace Characters
{
    class Human : BaseCharacter
    {
        // setup
        public HumanComponentCache HumanCache;
        public BaseUseable Special;
        public BaseUseable Weapon;
        public HookUseable HookLeft;
        public HookUseable HookRight;
        public HumanMountState MountState = HumanMountState.None;
        public HumanCarryState CarryState = HumanCarryState.None;
        public Horse Horse;
        public HumanSetup Setup;
        public HumanStats Stats;
        public bool FinishSetup;
        private HumanCustomSkinLoader _customSkinLoader;
        public override List<string> EmoteActions => new List<string>() { "Salute", "Wave", "Nod", "Shake", "Dance", "Eat", "Flip" };
        public static LayerMask AimMask = PhysicsLayer.GetMask(PhysicsLayer.TitanPushbox, PhysicsLayer.MapObjectProjectiles,
           PhysicsLayer.MapObjectEntities, PhysicsLayer.MapObjectAll);
        public static LayerMask ClipMask = PhysicsLayer.GetMask(PhysicsLayer.MapObjectAll, PhysicsLayer.MapObjectCharacters,
            PhysicsLayer.MapObjectEntities);
        private Dictionary<Renderer, Material> FPSMaterials = new Dictionary<Renderer, Material>();

        // state
        private HumanState _state = HumanState.Idle;
        public string CurrentSpecial;
        public BaseTitan Grabber;
        public Transform GrabHand;
        public Human Carrier;
        public Transform CarryBack;
        public Human BackHuman;
        public Vector3 CarryVelocity;
        public MapObject MountedMapObject;
        public Transform MountedTransform;
        public Vector3 MountedPositionOffset;
        public Vector3 MountedRotationOffset;
        public Vector3 _lastMountedPosition;
        public Vector3 _mountedVelocity;
        public bool CancelHookLeftKey;
        public bool CancelHookRightKey;
        public bool CancelHookBothKey;
        public bool CanDodge = true;
        public bool IsInvincible = true;
        public float InvincibleTimeLeft;
        public bool CanMountedAttack = false;
        public bool InMountedCombat = false;
        public bool IsAttackableState;
        public bool IsRefillable;
        private object[] _lastMountMessage = null;
        private int _lastCarryRPCSender = -1;
        private float _grabIFrames = 0f;
        private bool _bladeTrailActive;
        private int _bladeFireState;

        // physics
        public float ReelInAxis = 0f;
        public float ReelOutAxis = 0f;
        public float ReelOutScrollTimeLeft = 0f;
        public float TargetMagnitude = 0f;
        public bool IsWalk;
        public const float RealismMaxReel = 120f;
        public const float RealismDeathVelocity = 100f;
        private const float MaxVelocityChange = 10f;
        private float _originalDashSpeed;
        public Quaternion _targetRotation;
        private float _wallRunTime = 0f;
        private bool _wallJump = false;
        private bool _wallSlide = false;
        private bool _canWallSlideJump = false;
        private Vector3 _wallSlideGround = Vector3.zero;
        private bool _launchLeft;
        private bool _launchRight;
        private float _launchLeftTime;
        private float _launchRightTime;
        private bool _needLean;
        private bool _almostSingleHook;
        private bool _leanLeft;
        private bool _isTrigger;
        private bool _useFixedUpdateClipping = true;
        private Vector3 _lastPosition;
        private Vector3 _lastVelocity;
        private Vector3 _currentVelocity;
        private static LayerMask TitanDetectionMask = PhysicsLayer.GetMask(PhysicsLayer.ProjectileDetection);
        private LayerMask HumanGroundMaskLayers = PhysicsLayer.GetMask(PhysicsLayer.TitanPushbox, PhysicsLayer.MapObjectEntities,
            PhysicsLayer.MapObjectCharacters, PhysicsLayer.MapObjectHumans, PhysicsLayer.MapObjectAll);
        public override LayerMask GroundMask => HumanGroundMaskLayers;
        private Quaternion _oldHeadRotation = Quaternion.identity;
        public Vector2 LastGoodHeadAngle = Vector2.zero;
        public Quaternion? LateUpdateHeadRotation = Quaternion.identity;
        public Quaternion? LateUpdateHeadRotationRecv = Quaternion.identity;
        private const float CarryLagCompensationDistance = 100f;

        // actions
        public string StandAnimation;
        public string AttackAnimation;
        public bool _gunArmAim;
        public string RunAnimation;
        public bool _attackRelease;
        public bool _attackButtonRelease;
        public bool _reelInWaitForRelease;
        private float _stateTimeLeft = 0f;
        private float _dashTimeLeft = 0f;
        private bool _cancelGasDisable;
        private bool _animationStopped;
        private bool _needFinishReload;
        private float _reloadTimeLeft;
        private float _reloadCooldownLeft;
        private string _reloadAnimation;
        private float _dashCooldownLeft = 0f;
        private Human _hookHuman;
        private bool _hookHumanLeft;
        private float _hookHumanConstantTimeLeft;
        private bool _isReelingOut;
        private Dictionary<BaseTitan, float> _lastNapeHitTimes = new Dictionary<BaseTitan, float>();

        protected override void CreateDetection()
        {
            Detection = new HumanDetection(this);
        }

        public void DieChangeCharacter()
        {
            Cache.PhotonView.RPC("MarkDeadRPC", RpcTarget.AllBuffered, new object[0]);
            PhotonNetwork.Destroy(gameObject);
        }

        public void IsChangingPosition()
        {
            _useFixedUpdateClipping = false;
        }

        [PunRPC]
        public void BlowAwayRPC(Vector3 force, PhotonMessageInfo info)
        {
            if (info.photonView.IsMine)
                Cache.Rigidbody.AddForce(force, ForceMode.Impulse);
        }

        [PunRPC]
        public override void MarkDeadRPC(PhotonMessageInfo info)
        {
            if (info.Sender != Cache.PhotonView.Owner)
                return;
            Dead = true;
            if (Setup != null)
                Setup.DeleteDie();
            GetComponent<CapsuleCollider>().enabled = false;
            if (IsMine())
            {
                FalseAttack();
                SetCarrierTriggerCollider(false);
            }
        }

        [PunRPC]
        public virtual void UngrabRPC(PhotonMessageInfo info)
        {
            if (Grabber == null || info.Sender != Grabber.Cache.PhotonView.Owner)
                return;
            Ungrab(false, true);
        }

        public Ray GetAimRayAfterHuman()
        {
            Ray ray = SceneLoader.CurrentCamera.Camera.ScreenPointToRay(CursorManager.GetInGameMousePosition());

            // Define a plane at the characters position facing towards the camera's forward direction
            Plane plane = new Plane(ray.direction, Cache.Transform.position);

            // Find the distance from the ray origin to the plane along its direction
            float distance;
            plane.Raycast(ray, out distance);

            // Get the point on the plane that is distance units away from the ray origin

            Vector3 target = ray.GetPoint(distance);

            // Set the ray origin to the new found point on the plane
            ray.origin = target;

            return ray;
        }

        public Ray GetAimRayAfterHumanCheap()
        {
            Ray ray = SceneLoader.CurrentCamera.Camera.ScreenPointToRay(CursorManager.GetInGameMousePosition());
            // Move the ray origin along its direction by the distance between the ray origin and the character
            ray.origin = ray.GetPoint(Vector3.Distance(ray.origin, HumanCache.Head.transform.position));

            return ray;
        }

        public override Vector3 GetAimPoint()
        {
            RaycastHit hit;
            Ray ray = GetAimRayAfterHumanCheap(); // SceneLoader.CurrentCamera.Camera.ScreenPointToRay(Input.mousePosition);
            Vector3 target = ray.origin + ray.direction * 1000f;
            if (Physics.Raycast(ray, out hit, 1000f, AimMask.value))
                target = hit.point;
            return target;
        }

        public Vector3 GetAimPoint(Vector3 origin, Vector3 direction)
        {
            RaycastHit hit;
            Vector3 target = origin + direction * 1000f;
            if (Physics.Raycast(origin, direction, out hit, 1000f, AimMask.value))
                target = hit.point;
            return target;
        }

        private Vector2 GetLookAngle(Vector3 target)
        {
            Vector3 vector = target - Cache.Transform.position;
            float angle = -Mathf.Atan2(vector.z, vector.x) * Mathf.Rad2Deg;
            float verticalAngle = -Mathf.DeltaAngle(angle, Cache.Transform.rotation.eulerAngles.y - 90f);
            float y = HumanCache.Neck.position.y - target.y;
            float distance = Util.DistanceIgnoreY(target, HumanCache.Transform.position);
            float horizontalAngle = Mathf.Atan2(y, distance) * Mathf.Rad2Deg;
            return new Vector2(horizontalAngle, verticalAngle);
        }


        public bool CanJump()
        {
            return (Grounded && CarryState != HumanCarryState.Carry && (State == HumanState.Idle || State == HumanState.Slide) &&
                !Animation.IsPlaying(HumanAnimations.Jump) && !Animation.IsPlaying(HumanAnimations.HorseMount));
        }

        public void Jump()
        {
            Idle();
            CrossFade(HumanAnimations.Jump, 0.1f);
            PlaySound(HumanSounds.Jump);
            ToggleSparks(false);
        }

        public void Mount(Transform transform, Vector3 positionOffset, Vector3 rotationOffset, bool canMountedAttack = false)
        {
            Transform parent = transform;
            MapObject mapObject = null;
            string transformName = "";
            while (parent != null)
            {
                if (MapLoader.GoToMapObject.ContainsKey(parent.gameObject))
                {
                    mapObject = MapLoader.GoToMapObject[parent.gameObject];
                    break;
                }
                if (transformName == "")
                    transformName = parent.name;
                else
                    transformName = parent.name + "/" + transformName;
                parent = parent.parent;
            }
            Mount(mapObject, transformName, positionOffset, rotationOffset, canMountedAttack);
        }

        public void Mount(MapObject mapObject, Vector3 positionOffset, Vector3 rotationOffset, bool canMountedAttack = false)
        {
            Mount(mapObject, "", positionOffset, rotationOffset, canMountedAttack);
        }

        public void Mount(MapObject mapObject, string transformName, Vector3 positionOffset, Vector3 rotationOffset, bool canMountedAttack = false)
        {
            if (MountedTransform != transform)
            {
                Unmount(true);
                SetInterpolation(false);
                SetTriggerCollider(true);
            }
            int scriptId = -100;
            if (mapObject != null)
                scriptId = mapObject.ScriptObject.Id;
            _lastMountMessage = new object[] { scriptId, transformName, positionOffset, rotationOffset, canMountedAttack };
            Cache.PhotonView.RPC("MountRPC", RpcTarget.All, _lastMountMessage);
        }

        [PunRPC]
        public void MountRPC(int mapObjectID, string transformName, Vector3 positionOffset, Vector3 rotationOffset, bool canMountedAttack, PhotonMessageInfo info)
        {
            if (info.Sender != Cache.PhotonView.Owner)
                return;
            CanMountedAttack = canMountedAttack;
            MountState = HumanMountState.MapObject;
            MountedMapObject = null;
            MountedTransform = null;
            if (_inGameManager.IsFinishedLoading())
                FinishMount(mapObjectID, transformName, positionOffset, rotationOffset);
            else
                StartCoroutine(WaitAndMount(mapObjectID, transformName, positionOffset, rotationOffset));
        }

        private IEnumerator WaitAndMount(int mapObjectID, string transformName, Vector3 positionOffset, Vector3 rotationOffset)
        {
            while (!_inGameManager.IsFinishedLoading())
                yield return null;
            if (MountState == HumanMountState.MapObject)
                FinishMount(mapObjectID, transformName, positionOffset, rotationOffset);
        }

        private void FinishMount(int mapObjectID, string transformName, Vector3 positionOffset, Vector3 rotationOffset)
        {
            if (mapObjectID == -100)
                return;
            if (MapLoader.IdToMapObject.ContainsKey(mapObjectID))
            {
                var mapObject = MapLoader.IdToMapObject[mapObjectID];
                Transform transform;
                if (transformName != "")
                    transform = mapObject.GameObject.transform.Find(transformName);
                else
                    transform = mapObject.GameObject.transform;
                if (transform != null)
                {
                    MountedMapObject = mapObject;
                    MountedTransform = transform;
                    MountedPositionOffset = positionOffset;
                    MountedRotationOffset = rotationOffset;
                    _lastMountedPosition = MountedTransform.TransformPoint(MountedPositionOffset);
                }
            }
        }

        public void Unmount(bool immediate)
        {
            SetInterpolation(true);
            if (MountState != HumanMountState.None && !immediate)
            {
                PlayAnimation(HumanAnimations.HorseDismount);
                Cache.Rigidbody.AddForce((Vector3.up * 10f) - (Cache.Transform.forward * 2f) - (Cache.Transform.right * 1f), ForceMode.VelocityChange);
            }
            else
            {
                Idle();
            }
            if (MountState == HumanMountState.MapObject)
                SetTriggerCollider(false);
            MountState = HumanMountState.None;
            _lastMountMessage = null;
            Cache.PhotonView.RPC("UnmountRPC", RpcTarget.All, new object[0]);
        }

        [PunRPC]
        public void UnmountRPC(PhotonMessageInfo info)
        {
            if (info.Sender != Cache.PhotonView.Owner)
                return;
            MountState = HumanMountState.None;
            CanMountedAttack = false;
            MountedTransform = null;
            MountedMapObject = null;
        }

        public void MountHorse()
        {
            if (Horse != null && MountState == HumanMountState.None && Vector3.Distance(Horse.Cache.Transform.position, Cache.Transform.position) < 15f)
            {
                PlayAnimation(HumanAnimations.HorseMount);
                TargetAngle = Horse.transform.rotation.eulerAngles.y;
                PlaySound(HumanSounds.Dodge);
            }
        }

        public void Dodge(float targetAngle)
        {
            if (CanDodge)
            {
                State = HumanState.GroundDodge;
                TargetAngle = targetAngle;
                _targetRotation = GetTargetRotation();
                CrossFade(HumanAnimations.Dodge, 0.1f);
                PlaySound(HumanSounds.Dodge);
                ToggleSparks(false);
            }
        }

        public void DodgeWall()
        {
            FalseAttack();
            State = HumanState.GroundDodge;
            PlayAnimation(HumanAnimations.Dodge, 0.2f);
            ToggleSparks(false);
        }

        public void Dash(float targetAngle)
        {
            if (_dashTimeLeft <= 0f && Stats.CurrentGas > 0 && MountState == HumanMountState.None &&
                State != HumanState.Grab && CarryState != HumanCarryState.Carry && _dashCooldownLeft <= 0f)
            {
                Stats.UseDashGas();
                TargetAngle = targetAngle;
                Vector3 direction = GetTargetDirection();
                _originalDashSpeed = Cache.Rigidbody.velocity.magnitude;
                _targetRotation = GetTargetRotation();
                if (!_wallSlide)
                {
                    //The line below was causing problems when dashing away from walls at certain angles.
                    //Removing it fixed that but I'm unsure if it's needed for another situation (didn't notice a difference without it), do uncomment if the case
                    //Cache.Rigidbody.rotation = _targetRotation;
                    CrossFade(HumanAnimations.Dash, 0.1f, 0.1f);
                }

                else
                    PlayAnimation(HumanAnimations.Dodge, 0.2f);
                EffectSpawner.Spawn(EffectPrefabs.GasBurst, Cache.Transform.position, Cache.Transform.rotation);
                PlaySound(HumanSounds.GasBurst);
                _dashTimeLeft = 0.5f;

                State = HumanState.AirDodge;
                FalseAttack();
                Cache.Rigidbody.AddForce(direction * 40f, ForceMode.VelocityChange);
                _dashCooldownLeft = 0.2f;
                ((InGameMenu)UIManager.CurrentMenu).HUDBottomHandler.ShakeGas();
            }
        }

        public void DashVertical(float targetAngle, Vector3 direction)
        {
            if (_dashTimeLeft <= 0f && Stats.CurrentGas > 0 && MountState == HumanMountState.None &&
                State != HumanState.Grab && CarryState != HumanCarryState.Carry && _dashCooldownLeft <= 0f)
            {
                Stats.UseDashGas();
                TargetAngle = targetAngle;
                _originalDashSpeed = Cache.Rigidbody.velocity.magnitude;
                _targetRotation = Quaternion.LookRotation(direction);
                Cache.Rigidbody.rotation = _targetRotation;
                EffectSpawner.Spawn(EffectPrefabs.GasBurst, Cache.Transform.position, Cache.Transform.rotation);
                PlaySound(HumanSounds.GasBurst);
                _dashTimeLeft = 0.5f;
                CrossFade(HumanAnimations.Dash, 0.1f, 0.1f);
                State = HumanState.AirDodge;
                FalseAttack();
                Cache.Rigidbody.AddForce(direction * 40f, ForceMode.VelocityChange);
                _dashCooldownLeft = 0.2f;
                ((InGameMenu)UIManager.CurrentMenu).HUDBottomHandler.ShakeGas();
            }
        }

        public void Idle()
        {
            if (State == HumanState.Attack || State == HumanState.SpecialAttack)
                FalseAttack();
            State = HumanState.Idle;
            CrossFade(StandAnimation, 0.1f);
        }
        public void Grab(BaseTitan grabber, string type)
        {
            if (HasGrabImmunity())
                return;
            if (MountState != HumanMountState.None)
                Unmount(true);
            Transform hand;
            if (type == "GrabLeft")
                hand = grabber.BaseTitanCache.GrabLSocket;
            else
                hand = grabber.BaseTitanCache.GrabRSocket;
            HookLeft.DisableAnyHook();
            HookRight.DisableAnyHook();
            UnhookHuman(true);
            UnhookHuman(false);
            State = HumanState.Grab;
            grabber.Cache.PhotonView.RPC("GrabRPC", RpcTarget.All, new object[] { Cache.PhotonView.ViewID, type == "GrabLeft" });
            SetTriggerCollider(true);
            FalseAttack();
            Grabber = grabber;
            GrabHand = hand;
            Cache.PhotonView.RPC("SetSmokeRPC", RpcTarget.All, new object[] { false });
            PlayAnimation(HumanAnimations.Grabbed);
            ToggleSparks(false);
            var windEmission = HumanCache.Wind.emission;
            windEmission.enabled = false;
            if (IsMainCharacter())
                MusicManager.PlayGrabbedSong();
        }

        public void Ungrab(bool notifyTitan, bool idle)
        {
            if (notifyTitan && Grabber != null)
                Grabber.Cache.PhotonView.RPC("UngrabRPC", RpcTarget.All, new object[] { Cache.PhotonView.ViewID });
            Grabber = null;
            GrabHand = null;
            SetTriggerCollider(false);
            if (idle)
                Idle();
            if (IsMainCharacter())
                MusicManager.OnEscapeGrab();
        }

        public void Carry(Human carrier, Transform back)
        {
            if (MountState != HumanMountState.None)
                Unmount(true);
            HookLeft.DisableAnyHook();
            HookRight.DisableAnyHook();
            UnhookHuman(true);
            UnhookHuman(false);
            CarryState = HumanCarryState.Carry;
            SetTriggerCollider(true);
            FalseAttack();
            Carrier = carrier;
            CarryBack = back;
            SetCarrierTriggerCollider(true);
            Cache.PhotonView.RPC("SetSmokeRPC", RpcTarget.All, new object[] { false });
            ToggleSparks(false);
            State = HumanState.Idle;
            CrossFade(StandAnimation, 0.01f);
        }

        [PunRPC]
        public void CarryRPC(int initiatorViewId, PhotonMessageInfo info)
        {
            var initiatorView = PhotonView.Find(initiatorViewId);
            if (initiatorView.Owner != info.Sender)
                return;
            var initiator = initiatorView.GetComponent<Human>();
            if (initiator == null)
                return;
            _lastCarryRPCSender = initiatorViewId;
            if (Cache.PhotonView.IsMine && IsCarryableBy(initiator))
                Cache.PhotonView.RPC("ConfirmCarryRPC", RpcTarget.All, new object[] { initiatorViewId, Cache.PhotonView.ViewID });
        }

        [PunRPC]
        public void ConfirmCarryRPC(int initiatorViewId, int targetViewId, PhotonMessageInfo info)
        {
            var targetView = PhotonView.Find(targetViewId);
            if (targetView.Owner != info.Sender)
                return;
            if (_lastCarryRPCSender != initiatorViewId)
                return;
            var initiatorView = PhotonView.Find(initiatorViewId);
            if (initiatorView == null)
                return;
            var initiator = initiatorView.GetComponent<Human>();
            if (initiator == null)
                return;
            Carrier = initiator;
            Carrier.BackHuman = this;
            CarryState = HumanCarryState.Carry;
            _lastCarryRPCSender = -1;
            if (Cache.PhotonView.IsMine)
                Carry(Carrier, Carrier.Cache.Transform);
        }

        public void Uncarry()
        {
            SetCarrierTriggerCollider(false);
            SetTriggerCollider(false);
            SetVelocityFromCarrier();
            Cache.Rigidbody.AddForce((((Vector3.up * 10f) - (Cache.Transform.forward * 2f)) - (Cache.Transform.right * 1f)), ForceMode.VelocityChange);
        }

        [PunRPC]
        public virtual void UncarryRPC(PhotonMessageInfo info)
        {
            CarryState = HumanCarryState.None;
            if (Cache.PhotonView.IsMine)
                Uncarry();
            if (Carrier != null)
                Carrier.BackHuman = null;
            Carrier = null;
        }

        public void SetCarrierTriggerCollider(bool trigger)
        {
            if (Carrier != null)
                Carrier.Cache.Colliders[0].isTrigger = trigger;
        }

        public void SetVelocityFromCarrier()
        {
            if (Carrier != null)
                Cache.Rigidbody.velocity = Carrier.CarryVelocity;
        }

        public Human GetHumanAlongRay(Ray ray, float distance)
        {
            Human human = null;
            float minDistance = float.PositiveInfinity;

            RaycastHit[] hits = Physics.RaycastAll(ray, distance, PhysicsLayer.GetMask(PhysicsLayer.Human, PhysicsLayer.MapObjectCharacters));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.gameObject.GetComponent<Human>() is Human h)
                {
                    if (IsValidCarryTarget(h, distance))
                    {
                        float d = Vector3.Distance(Cache.Transform.position, h.Cache.Transform.position);
                        if (d < minDistance)
                        {
                            human = h;
                            minDistance = d;
                        }
                    }
                }
            }

            return human;
        }

        public bool IsValidCarryTarget(Human human, float distance)
        {
            return human != null && human != this && human.CarryState == HumanCarryState.None && human.Carrier == null
                && human.BackHuman == null && TeamInfo.SameTeam(human, Team) && Vector3.Distance(human.Cache.Transform.position, Cache.Transform.position) < distance;
        }

        public bool IsCarryable(Human human)
        {
            return human != null && human != this && human.CarryState == HumanCarryState.None && human.Carrier == null && human.BackHuman == null;
        }

        private bool IsCarryableBy(Human initiator)
        {
            if (initiator == null)
                return false;
            bool isFree = CarryState == HumanCarryState.None && Carrier == null && BackHuman == null;
            bool isInitiatorFree = initiator.CarryState == HumanCarryState.None && initiator.Carrier == null && initiator.BackHuman == null;
            float distance = Vector3.Distance(initiator.Cache.Transform.position, Cache.Transform.position);
            bool isWithinDistance = distance < CarrySpecial.DefaultCarryDistance + CarryLagCompensationDistance;
            return isFree && isInitiatorFree && isWithinDistance;
        }

        /// <summary>
        /// Gets the carry target first by aimpoint, then by distance if that fails.
        /// </summary>
        /// <param name="distance">Max range allowed for carry target.</param>
        /// <returns>Human carry option or null if none exists.</returns>
        public Human GetCarryOption(float distance)
        {
            RaycastHit hit;
            Human target = GetHumanAlongRay(GetAimRayAfterHumanCheap(), distance);
            if (IsValidCarryTarget(target, distance))
            {
                return target;
            }

            // Otherwise, find the nearest valid carry target within the distance.
            float nearestDistance = float.PositiveInfinity;
            Human nearestHuman = null;
            foreach (Human carryTarget in _inGameManager.Humans)
            {
                if (!IsValidCarryTarget(carryTarget, distance))
                {
                    continue;
                }
                float targetDistance = Vector3.Distance(Cache.Transform.position, carryTarget.Cache.Transform.position);
                if (targetDistance < nearestDistance)
                {
                    nearestHuman = carryTarget;
                    nearestDistance = targetDistance;
                }
            }
            return nearestHuman;
        }


        public void StartCarrySpecial(Human target)
        {
            ClearAllActionsForSpecial();
            target.Cache.PhotonView.RPC("CarryRPC", RpcTarget.All, new object[] { Cache.PhotonView.ViewID });
        }

        public void StopCarrySpecial()
        {
            ClearAllActionsForSpecial();
            if (BackHuman != null)
            {
                BackHuman.Cache.PhotonView.RPC("UncarryRPC", RpcTarget.All, new object[0]);
            }
        }

        public void ClearAllActionsForSpecial()
        {
            this.CancelHookBothKey = true;
            this.CancelHookLeftKey = true;
            this.CancelHookRightKey = true;
            this.HookLeft.SetInput(false);
            this.HookRight.SetInput(false);
            this.HookLeft.DisableAnyHook();
            this.HookRight.DisableAnyHook();
        }

        /*public void StartSpecialCarry(float distance)
        {
            Human human = FindNearestHuman();
            if (BackHuman != null)
            {
                BackHuman.Cache.PhotonView.RPC("UncarryRPC", RpcTarget.All, new object[0]);
            }
            else if (human != null && human.CarryState == HumanCarryState.None && human.Carrier == null && human.BackHuman == null
                && Vector3.Distance(human.Cache.Transform.position, Cache.Transform.position) < distance)
            {
                human.Cache.PhotonView.RPC("CarryRPC", RpcTarget.All, new object[] { Cache.PhotonView.ViewID });
            }
        }*/

        public void SpecialActionState(float time)
        {
            State = HumanState.SpecialAction;
            _stateTimeLeft = time;
        }

        public void TransformShifter(string shifter, float liveTime)
        {
            _inGameManager.SpawnPlayerShifterAt(shifter, liveTime, Cache.Transform.position, Cache.Transform.rotation.eulerAngles.y);
            var character = (BaseShifter)_inGameManager.CurrentCharacter;
            character.PreviousHumanGas = Stats.CurrentGas;
            character.PreviousHumanWeapon = Weapon;
            PhotonNetwork.LocalPlayer.SetCustomProperty(PlayerProperty.CharacterViewId, character.Cache.PhotonView.ViewID);
            PhotonNetwork.Destroy(gameObject);
        }

        public IEnumerator WaitAndTransformFromShifter(float previousHumanGas, BaseUseable previousHumanWeapon)
        {
            while (!FinishSetup)
            {
                yield return null;
            }
            Stats.CurrentGas = previousHumanGas;
            if (previousHumanWeapon is BladeWeapon)
            {
                BladeWeapon previousBlade = (BladeWeapon)previousHumanWeapon;
                BladeWeapon weapon = (BladeWeapon)Weapon;
                weapon.BladesLeft = previousBlade.BladesLeft;
                weapon.CurrentDurability = previousBlade.CurrentDurability;
                if (weapon.CurrentDurability == 0)
                {
                    ToggleBlades(false);
                }
            }
            else if (previousHumanWeapon is AmmoWeapon)
            {
                if (previousHumanWeapon is AHSSWeapon)
                {
                    ((AHSSWeapon)previousHumanWeapon).HandleUI();
                }
                AmmoWeapon previousAmmoWeapon = (AmmoWeapon)previousHumanWeapon;
                AmmoWeapon weapon = (AmmoWeapon)Weapon;
                weapon.RoundLeft = previousAmmoWeapon.RoundLeft;
                weapon.AmmoLeft = previousAmmoWeapon.AmmoLeft;
                if (weapon.RoundLeft == 0 && Weapon is ThunderspearWeapon)
                    SetThunderspears(false, false);
            }
        }

        public void Reload()
        {
            if ((Setup.Weapon == HumanWeapon.AHSS || Setup.Weapon == HumanWeapon.APG) && !SettingsManager.InGameCurrent.Misc.GunsAirReload.Value && !Grounded)
                return;
            if (_needFinishReload || _reloadCooldownLeft > 0f)
                return;
            if (Weapon is AmmoWeapon)
            {
                if (((AmmoWeapon)Weapon).AmmoLeft <= 0)
                    return;
                if (Weapon is AHSSWeapon)
                {
                    ToggleBlades(false);
                    CancelHookLeftKey = true;
                    CancelHookRightKey = true;
                    CancelHookBothKey = true;
                }
                else if (Weapon is ThunderspearWeapon)
                {
                    SetThunderspears(false, false);
                    CancelHookLeftKey = true;
                    CancelHookRightKey = true;
                    CancelHookBothKey = true;
                }
                PlaySound(HumanSounds.GunReload);
            }
            else if (Weapon is BladeWeapon)
            {
                if (((BladeWeapon)Weapon).BladesLeft <= 0)
                    return;
                ToggleBlades(false);
                if (Grounded)
                    PlaySound(HumanSounds.BladeReloadGround);
                else
                    PlaySound(HumanSounds.BladeReloadAir);
            }
            if (Setup.Weapon == HumanWeapon.AHSS || Setup.Weapon == HumanWeapon.Thunderspear || Setup.Weapon == HumanWeapon.APG)
            {
                if (Grounded)
                    _reloadAnimation = HumanAnimations.AHSSGunReloadBoth;
                else
                    _reloadAnimation = HumanAnimations.AHSSGunReloadBothAir;
            }
            else
            {
                if (Grounded)
                    _reloadAnimation = HumanAnimations.ChangeBlade;
                else
                    _reloadAnimation = HumanAnimations.ChangeBladeAir;
            }
            CrossFade(_reloadAnimation, 0.1f, 0f);
            State = HumanState.Reload;
            _stateTimeLeft = Animation.GetTotalTime(_reloadAnimation);
            _needFinishReload = true;
            _reloadTimeLeft = _stateTimeLeft;
            _reloadCooldownLeft = _reloadTimeLeft + 0.5f;
            ((InGameMenu)UIManager.CurrentMenu).HUDBottomHandler.Reload();
        }

        protected void FinishReload()
        {
            if (!_needFinishReload)
                return;
            _needFinishReload = false;
            Weapon.Reload();
            if (Weapon is BladeWeapon || Weapon is AHSSWeapon || Weapon is APGWeapon)
            {
                ToggleBlades(true);
            }
            else if (Weapon is ThunderspearWeapon)
                SetThunderspears(true, true);
        }

        public bool Refill()
        {
            if (!IsRefillable)
                return false;
            State = HumanState.Refill;
            if (Special is SupplySpecial)
            {
                ((SupplySpecial)Special).Reset();
            }
            ToggleSparks(false);
            CrossFade(HumanAnimations.Refill, 0.1f);
            PlaySound(HumanSounds.Refill);
            _stateTimeLeft = Animation.GetTotalTime(HumanAnimations.Refill);
            return true;
        }
        public bool SupplySpawnableRefill()
        {
            if (!IsRefillable)
                return false;
            State = HumanState.Refill;
            ToggleSparks(false);
            CrossFade(HumanAnimations.Refill, 0.1f);
            PlaySound(HumanSounds.Refill);
            _stateTimeLeft = Animation.GetTotalTime(HumanAnimations.Refill);
            return true;
        }
        public bool NeedRefill(bool isGasTank)
        {
            if (Stats.CurrentGas < Stats.MaxGas)
            {
                return true;
            }
            if (isGasTank && Special is SupplySpecial && Special.UsesLeft <= 0)
            {
                return true;
            }
            if (Weapon is BladeWeapon)
            {
                var weapon = (BladeWeapon)Weapon;
                return weapon.BladesLeft < weapon.MaxBlades || weapon.CurrentDurability < weapon.MaxDurability;
            }
            else if (Weapon is AmmoWeapon)
            {
                var weapon = (AmmoWeapon)Weapon;
                return weapon.NeedRefill();
            }
            return false;
        }

        public void FinishRefill()
        {
            if (Weapon == null || Dead)
                return;
            if (Weapon is BladeWeapon)
            {
                ToggleBlades(true);
            }
            Weapon.Reset();
            Stats.CurrentGas = Stats.MaxGas;
        }

        public override void Emote(string emote)
        {
            if (CanEmote())
            {
                if (State == HumanState.Attack)
                    FalseAttack();
                string animation = HumanAnimations.EmoteSalute;
                if (emote == "Salute")
                    animation = HumanAnimations.EmoteSalute;
                else if (emote == "Dance")
                    animation = HumanAnimations.SpecialArmin;
                else if (emote == "Flip")
                    animation = HumanAnimations.Dodge;
                else if (emote == "Wave")
                    animation = HumanAnimations.EmoteWave;
                else if (emote == "Nod")
                    animation = HumanAnimations.EmoteYes;
                else if (emote == "Shake")
                    animation = HumanAnimations.EmoteNo;
                else if (emote == "Eat")
                    animation = HumanAnimations.SpecialSasha;
                EmoteAnimation(animation);
                ToggleSparks(false);
            }
        }

        public override void ForceAnimation(string animation, float fade)
        {
            if (State == HumanState.Attack)
                FalseAttack();
            State = HumanState.EmoteAction;
            CrossFade(animation, fade);
            _stateTimeLeft = Animation.GetTotalTime(animation);
        }

        public void EmoteAnimation(string animation)
        {
            State = HumanState.EmoteAction;
            CrossFade(animation, 0.1f);
            _stateTimeLeft = Animation.GetTotalTime(animation);
            ToggleSparks(false);
        }

        public bool CanEmote()
        {
            return !Dead && State != HumanState.Grab && CarryState != HumanCarryState.Carry && State != HumanState.AirDodge && State != HumanState.EmoteAction && State != HumanState.SpecialAttack && IsAttackableState
                && State != HumanState.Stun;
        }

        public override Transform GetCameraAnchor()
        {
            return HumanCache.Head;
        }

        protected override void CreateCache(BaseComponentCache cache)
        {
            HumanCache = new HumanComponentCache(gameObject);
            base.CreateCache(HumanCache);
        }

        protected override IEnumerator WaitAndDie()
        {
            if (State == HumanState.Grab)
                PlaySound(HumanSounds.Death5);
            else
            {
                PlaySound(HumanSounds.Death2);
                MusicManager.PlayDeathSong();
            }
            EffectSpawner.Spawn(EffectPrefabs.Blood2, Cache.Transform.position, Cache.Transform.rotation);
            yield return new WaitForSeconds(2f);
            PhotonNetwork.Destroy(gameObject);
        }

        public void Init(bool ai, string team, InGameCharacterSettings settings)
        {
            base.Init(ai, team);
            Setup.Copy(settings);
            if (!ai)
                gameObject.AddComponent<HumanPlayerController>();
        }

        public void ReloadHuman(InGameCharacterSettings settings)
        {
            FinishSetup = false;
            Setup.Copy(settings);
            if (IsMine())
            {
                Cache.PhotonView.RPC("SetupRPC", RpcTarget.All, Setup.CustomSet.SerializeToJsonString(), (int)Setup.Weapon);
                LoadSkin();
                _cameraFPS = false;
            }
            ((InGameMenu)UIManager.CurrentMenu).HUDBottomHandler.SetBottomHUD(this);
        }


        protected override void Awake()
        {
            if (SceneLoader.SceneName == SceneName.CharacterEditor)
            {
                this.enabled = false;
                return;
            }
            base.Awake();
            HumanCache = (HumanComponentCache)Cache;
            Cache.Rigidbody.freezeRotation = true;
            Cache.Rigidbody.useGravity = false;
            if (gameObject.GetComponent<HumanSetup>() == null)
                Setup = gameObject.AddComponent<HumanSetup>();
            Setup = gameObject.GetComponent<HumanSetup>();
            Stats = new HumanStats(this);
            _customSkinLoader = gameObject.AddComponent<HumanCustomSkinLoader>();

            if (IsMine())
            {
                Cache.AudioSources[HumanSounds.GasStart].spatialBlend = 0;
                Cache.AudioSources[HumanSounds.GasLoop].spatialBlend = 0;
                Cache.AudioSources[HumanSounds.GasEnd].spatialBlend = 0;
            }
        }

        protected override void Start()
        {
            _inGameManager.RegisterCharacter(this);
            base.Start();
            SetInterpolation(true);
            if (IsMine())
            {
                InvincibleTimeLeft = SettingsManager.InGameCurrent.Misc.InvincibilityTime.Value;
                TargetAngle = Cache.Transform.eulerAngles.y;
                Cache.PhotonView.RPC("SetupRPC", RpcTarget.All, Setup.CustomSet.SerializeToJsonString(), (int)Setup.Weapon);
                if (SettingsManager.InGameCurrent.Misc.Horses.Value)
                {
                    Horse = (Horse)CharacterSpawner.Spawn(CharacterPrefabs.Horse, Cache.Transform.position + Vector3.right * 2f, Quaternion.Euler(0f, TargetAngle, 0f));
                    Horse.Init(this);
                }
                LoadSkin();
            }
        }

        public override void OnPlayerEnteredRoom(Player player)
        {
            base.OnPlayerEnteredRoom(player);
            if (IsMine())
            {
                Cache.PhotonView.RPC("SetTriggerColliderRPC", player, new object[] { _isTrigger });
                if (MountState == HumanMountState.MapObject && _lastMountMessage != null)
                    Cache.PhotonView.RPC("MountRPC", player, _lastMountMessage);
                if (BackHuman != null && BackHuman.CarryState == HumanCarryState.Carry)
                    BackHuman.Cache.PhotonView.RPC("CarryRPC", player, new object[] { Cache.PhotonView.ViewID });

                Cache.PhotonView.RPC("SetupRPC", player, Setup.CustomSet.SerializeToJsonString(), (int)Setup.Weapon);
                LoadSkin(player);
            }
        }

        [PunRPC]
        public override void GetHitRPC(int viewId, string name, int damage, string type, string collider)
        {
            if (Dead || IsInvincible)
                return;
            if (type == "TitanEat")
            {
                base.GetHitRPC(viewId, name, damage, type, collider);
                if (!Dead)
                    Ungrab(false, true);
            }
            else if (type.StartsWith("Grab"))
            {
                if (State == HumanState.Grab)
                    return;
                var titan = (BaseTitan)Util.FindCharacterByViewId(viewId);
                Grab(titan, type);
            }
            else if (type == "Hook")
            {
                var killerName = Util.FindCharacterByViewId(viewId).Name + "'s Hook";
                base.GetHitRPC(viewId, killerName, damage, type, collider);
            }
            else
                base.GetHitRPC(viewId, name, damage, type, collider);
        }

        public override void OnHit(BaseHitbox hitbox, object victim, Collider collider, string type, bool firstHit)
        {
            if (hitbox != null)
            {
                if (hitbox == HumanCache.BladeHitLeft || hitbox == HumanCache.BladeHitRight)
                    type = "Blade";
                else if (hitbox == HumanCache.AHSSHit)
                {
                    type = "AHSS";
                    if (((CapsuleCollider)HumanCache.AHSSHit._collider).radius == CharacterData.HumanWeaponInfo["AHSS"]["Radius"].AsFloat * 2f)
                    {
                        type = "AHSSDouble";
                    }
                }

                else if (hitbox == HumanCache.APGHit)
                    type = "APG";
            }
            int damage = (CarryState == HumanCarryState.Carry && Carrier != null)
                ? Mathf.Max((int)(Carrier.CarryVelocity.magnitude * 10f), 10)
                : Mathf.Max((int)(Cache.Rigidbody.velocity.magnitude * 10f), 10);
            if (type == "Blade")
            {
                if (!(victim is CustomLogicCollisionHandler))
                    EffectSpawner.Spawn(EffectPrefabs.Blood1, hitbox.transform.position, Quaternion.Euler(270f, 0f, 0f));
                if (SettingsManager.SoundSettings.OldBladeEffect.Value)
                    PlaySound(HumanSounds.OldBladeHit);
                else
                    PlaySound(HumanSounds.BladeHit);
                var weapon = (BladeWeapon)Weapon;
                if (Stats.Perks["AdvancedAlloy"].CurrPoints == 1)
                {
                    if (damage < 500)
                        weapon.UseDurability(weapon.CurrentDurability);
                }
                else
                    weapon.UseDurability(2f);
                if (weapon.CurrentDurability == 0f)
                {
                    ToggleBlades(false);
                    PlaySound(HumanSounds.BladeBreak);
                }
                damage = (int)(damage * CharacterData.HumanWeaponInfo["Blade"]["DamageMultiplier"].AsFloat);
            }
            else if (type == "AHSS")
            {
                damage = (int)(damage * CharacterData.HumanWeaponInfo["AHSS"]["DamageMultiplier"].AsFloat);
            }
            else if (type == "AHSSDouble")
                type = "AHSS";
            else if (type == "APG")
                damage = (int)(damage * CharacterData.HumanWeaponInfo["APG"]["DamageMultiplier"].AsFloat);
            damage = Mathf.Max(damage, 10);
            if (CustomDamageEnabled)
                damage = CustomDamage;
            if (victim is CustomLogicCollisionHandler)
            {
                Vector3 position = Vector3.zero;
                if (hitbox != null)
                    position = hitbox.transform.position;
                (victim as CustomLogicCollisionHandler).GetHit(this, Name, damage, type, position);
                return;
            }
            var victimChar = (BaseCharacter)victim;
            if (!victimChar.Dead)
            {
                if (victimChar is BaseTitan)
                {
                    var titan = (BaseTitan)victimChar;
                    if (titan.BaseTitanCache.NapeHurtbox == collider)
                    {
                        if (type == "Blade" && !titan.CheckNapeAngle(hitbox.transform.position, CharacterData.HumanWeaponInfo["Blade"]["RestrictAngle"].AsFloat))
                            return;
                        if (type == "AHSS" && !titan.CheckNapeAngle(hitbox.transform.position, CharacterData.HumanWeaponInfo["AHSS"]["RestrictAngle"].AsFloat))
                            return;
                        if (type == "APG" && !titan.CheckNapeAngle(hitbox.transform.position, CharacterData.HumanWeaponInfo["APG"]["RestrictAngle"].AsFloat))
                            return;
                        if (type != "APG" && _lastNapeHitTimes.ContainsKey(titan) && (_lastNapeHitTimes[titan] + 0.2f) > Time.time)
                            return;
                        ((InGameMenu)UIManager.CurrentMenu).ShowKillScore(damage);
                        ((InGameCamera)SceneLoader.CurrentCamera).TakeSnapshot(titan.BaseTitanCache.Neck.position, damage);
                        if (type == "Blade" && SettingsManager.GraphicsSettings.BloodSplatterEnabled.Value)
                            ((InGameMenu)UIManager.CurrentMenu).ShowBlood();
                        if (type == "Blade" || type == "AHSS" || type == "APG")
                        {
                            if (SettingsManager.SoundSettings.OldNapeEffect.Value)
                                PlaySound(HumanSounds.OldNapeHit);
                            else
                            {
                                if (type == "APG")
                                    PlaySound(HumanSounds.NapeHit);
                                if (type == "Blade")
                                {
                                    if (damage < 500)
                                        PlaySound(HumanSounds.NapeHit);
                                    if (damage < 1000)
                                        PlaySound(HumanSounds.GetRandomBladeNapeVar1());
                                    else if (damage < 2000)
                                        PlaySound(HumanSounds.GetRandomBladeNapeVar2());
                                    else if (damage < 3000)
                                        PlaySound(HumanSounds.GetRandomBladeNapeVar3());
                                    else
                                        PlaySound(HumanSounds.GetRandomBladeNapeVar4());
                                }
                                else if (type == "AHSS")
                                {
                                    if (damage < 1000)
                                    {
                                        PlaySound(HumanSounds.NapeHit);
                                    }
                                    else if (damage < 2000)
                                    {
                                        PlaySound(HumanSounds.GetRandomAHSSNapeHitVar1());
                                    }
                                    else
                                    {
                                        PlaySound(HumanSounds.GetRandomAHSSNapeHitVar2());
                                    }
                                }

                            }

                        }
                        _lastNapeHitTimes[titan] = Time.time;
                    }
                    if (titan.BaseTitanCache.Hurtboxes.Contains(collider))
                    {
                        EffectSpawner.Spawn(EffectPrefabs.CriticalHit, hitbox.transform.position, Quaternion.Euler(270f, 0f, 0f));
                        victimChar.GetHit(this, damage, type, collider.name);
                        if (titan.BaseTitanCache.NapeHurtbox != collider)
                            PlaySound(HumanSounds.LimbHit);
                    }
                }
                else
                {
                    ((InGameMenu)UIManager.CurrentMenu).ShowKillScore(damage);
                    ((InGameCamera)SceneLoader.CurrentCamera).TakeSnapshot(victimChar.Cache.Transform.position, damage);
                    victimChar.GetHit(this, damage, type, collider.name);
                }
            }
        }

        protected void Update()
        {
            if (IsMine() && !Dead)
            {
                InMountedCombat = MountState != HumanMountState.None && CanMountedAttack;
                IsAttackableState = MountState == HumanMountState.None || InMountedCombat;
                IsRefillable = State == HumanState.Idle && (Grounded || InMountedCombat);
                _stateTimeLeft -= Time.deltaTime;
                _dashCooldownLeft -= Time.deltaTime;
                _reloadCooldownLeft -= Time.deltaTime;
                UpdateIFrames();
                UpdateBladeFire();
                if (_needFinishReload)
                {
                    _reloadTimeLeft -= Time.deltaTime;
                    if (Weapon is BladeWeapon)
                    {
                        if (Grounded && (Animation.GetNormalizedTime(_reloadAnimation) > 0.5f || _reloadTimeLeft <= 0f))
                            FinishReload();
                        else if (!Grounded && (Animation.GetNormalizedTime(_reloadAnimation) > 0.56f || _reloadTimeLeft <= 0f))
                            FinishReload();
                    }
                    else
                    {
                        if (Animation.GetNormalizedTime(_reloadAnimation) > 0.62f || _reloadTimeLeft <= 0f)
                            FinishReload();
                    }
                }
                if (State == HumanState.Grab)
                {
                    if (Grabber == null || Grabber.Dead)
                        Ungrab(false, true);
                }
                else if (MountState == HumanMountState.MapObject)
                {
                    if (MountedTransform == null)
                        Unmount(true);
                    else
                    {
                        Cache.Transform.position = MountedTransform.TransformPoint(MountedPositionOffset);
                        if (!IsAttackableState || (_state != HumanState.Attack && _state != HumanState.SpecialAttack && _state != HumanState.SpecialAction))
                        {
                            Cache.Transform.rotation = Quaternion.Euler(MountedTransform.rotation.eulerAngles + MountedRotationOffset);
                        }
                    }
                }
                else if (MountState == HumanMountState.Horse)
                {
                    if (Horse == null)
                        Unmount(true);
                    else
                    {
                        Cache.Transform.position = Horse.Cache.Transform.position + Vector3.up * 1.95f;
                        if (!IsAttackableState || (_state != HumanState.Attack && _state != HumanState.SpecialAttack && _state != HumanState.SpecialAction))
                        {
                            Cache.Transform.rotation = Horse.Cache.Transform.rotation;
                        }
                    }
                }

                if (State == HumanState.Attack)
                {
                    if (Setup.Weapon == HumanWeapon.Blade)
                    {
                        var bladeWeapon = (BladeWeapon)Weapon;
                        if (MountState != HumanMountState.None && IsAttackableState)
                        {
                            // This allows bladers to attack enemies on a different plane
                            var target = GetAimPoint();
                            var start = Cache.Transform.position + Cache.Transform.up * 0.8f;
                            var direction = (target - start).normalized;
                            var forward = MountedTransform.forward;
                            float maxAngle = 70f;
                            float angle = Vector3.Angle(forward, direction);

                            if (angle > maxAngle)
                            {
                                // Rotate the direction vector to the legal range
                                Quaternion rotation = Quaternion.AngleAxis(maxAngle * Mathf.Sign(Vector3.SignedAngle(forward, direction, Vector3.up)), Vector3.up);
                                direction = rotation * forward;
                            }
                            Cache.Transform.rotation = Quaternion.Lerp(Cache.Transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 10.0f);
                        }
                        if (!bladeWeapon.IsActive)
                            _attackButtonRelease = true;
                        if (!_attackRelease)
                        {
                            if (_attackButtonRelease)
                            {
                                ContinueAnimation();
                                _attackRelease = true;
                            }
                            else if (Animation.GetNormalizedTime(AttackAnimation) >= 0.32f)
                                PauseAnimation();
                        }
                        float startTime;
                        float endTime;
                        if (bladeWeapon.CurrentDurability <= 0f)
                            startTime = endTime = -1f;
                        else if (AttackAnimation == HumanAnimations.Attack4)
                        {
                            startTime = 0.6f;
                            endTime = 0.9f;
                        }
                        else
                        {
                            startTime = 0.5f;
                            endTime = 0.85f;
                        }
                        bool hold = SettingsManager.GraphicsSettings.WeaponTrailHold.Value;
                        float currTime = Animation.GetNormalizedTime(AttackAnimation);
                        if (currTime > startTime && currTime < endTime)
                        {
                            if (!HumanCache.BladeHitLeft.IsActive())
                            {
                                HumanCache.BladeHitLeft.Activate();
                                if (SettingsManager.SoundSettings.OldBladeEffect.Value)
                                    PlaySound(HumanSounds.OldBladeSwing);
                                else
                                {
                                    int random = UnityEngine.Random.Range(1, 5);
                                    PlaySound("BladeSwing" + random.ToString());
                                }
                                if (!hold)
                                    ToggleBladeTrails(true);
                            }
                            if (!HumanCache.BladeHitRight.IsActive())
                                HumanCache.BladeHitRight.Activate();
                        }
                        else if (HumanCache.BladeHitLeft.IsActive())
                        {
                            HumanCache.BladeHitLeft.Deactivate();
                            HumanCache.BladeHitRight.Deactivate();
                            if (!hold)
                                ToggleBladeTrails(false);
                        }
                        if (hold)
                        {
                            if (currTime > 0f && currTime < endTime)
                                ToggleBladeTrails(true);
                            else
                                ToggleBladeTrails(false);
                        }
                        if (Animation.GetNormalizedTime(AttackAnimation) >= 1f)
                            Idle();
                    }
                    else if (Setup.Weapon == HumanWeapon.AHSS || Setup.Weapon == HumanWeapon.Thunderspear || Setup.Weapon == HumanWeapon.APG)
                    {
                        if (Animation.GetNormalizedTime(AttackAnimation) >= 1f)
                            Idle();
                    }
                }
                else if (State == HumanState.EmoteAction || State == HumanState.SpecialAction || State == HumanState.Stun)
                {
                    if (_stateTimeLeft <= 0f)
                        Idle();
                }
                else if (State == HumanState.GroundDodge)
                {
                    if (Animation.IsPlaying(HumanAnimations.Dodge))
                    {
                        if (!(Grounded || (Animation.GetNormalizedTime(HumanAnimations.Dodge) <= 0.6f)))
                            Idle();
                        if (Animation.GetNormalizedTime(HumanAnimations.Dodge) >= 1f)
                            Idle();
                    }
                }
                else if (State == HumanState.Land)
                {
                    if (Animation.IsPlaying(HumanAnimations.Land) && (Animation.GetNormalizedTime(HumanAnimations.Land) >= 1f))
                        Idle();
                }
                else if (State == HumanState.Refill)
                {
                    if (_stateTimeLeft <= 0f)
                    {
                        Idle();
                        FinishRefill();
                    }
                }
                else if (State == HumanState.Reload)
                {
                    if (_stateTimeLeft <= 0f)
                        Idle();
                }
                else if (State == HumanState.Slide)
                {
                    if (!Grounded)
                        Idle();
                }
                else if (State == HumanState.AirDodge)
                {
                    if (_dashTimeLeft > 0f)
                    {
                        _dashTimeLeft -= Time.deltaTime;
                        if (Cache.Rigidbody.velocity.magnitude > _originalDashSpeed)
                            Cache.Rigidbody.AddForce(-Cache.Rigidbody.velocity * Time.deltaTime * 1.7f, ForceMode.VelocityChange);
                    }
                    else
                        Idle();
                }
                if (CarryState == HumanCarryState.Carry)
                {
                    if (Carrier == null || Carrier.Dead)
                        Cache.PhotonView.RPC("UncarryRPC", RpcTarget.All, new object[0]);
                    else if (MountState != HumanMountState.None || State == HumanState.Grab)
                        Cache.PhotonView.RPC("UncarryRPC", RpcTarget.All, new object[0]);
                    else
                    {
                        Vector3 offset = CarryBack.transform.forward * -0.4f + CarryBack.transform.up * 0.5f;
                        Cache.Transform.position = CarryBack.transform.position + offset;
                        Cache.Transform.rotation = CarryBack.transform.rotation;
                    }

                    if (Carrier != null && Vector3.Distance(Carrier.Cache.Transform.position, Cache.Transform.position) > 7f)
                        Cache.PhotonView.RPC("UncarryRPC", RpcTarget.All, new object[0]);
                }
            }
            if (GrabHand != null)
            {
                Cache.Transform.position = GrabHand.transform.position;
                Cache.Transform.rotation = GrabHand.transform.rotation;
            }
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (IsMine())
            {
                FixedUpdateLookTitan();
                FixedUpdateUseables();
                _isReelingOut = false;
                if (State == HumanState.Grab || Dead)
                {
                    Cache.Rigidbody.velocity = Vector3.zero;
                    if (IsPlayingSound(HumanSounds.GasLoop))
                    {
                        StopSound(HumanSounds.GasLoop);
                        ToggleSound(HumanSounds.GasEnd, true);
                    }
                    return;
                }
                if (CarryState == HumanCarryState.Carry)
                {
                    Cache.Rigidbody.velocity = Vector3.zero;
                    Grounded = false;
                    return;
                }
                if (MountState == HumanMountState.Horse)
                {
                    Cache.Rigidbody.velocity = Horse.Cache.Rigidbody.velocity;
                    if (!IsAttackableState)
                    {
                        return;
                    }
                }
                if (MountState == HumanMountState.MapObject)
                {
                    if (!IsAttackableState)
                    {
                        Cache.Rigidbody.velocity = Vector3.zero;
                        ToggleSparks(false);
                        if (State != HumanState.Idle)
                            Idle();
                        return;
                    }
                    else
                    {
                        var currentMountedPosition = MountedTransform.TransformPoint(MountedPositionOffset);
                        Cache.Rigidbody.velocity = (currentMountedPosition - _lastMountedPosition) / Time.deltaTime;
                        _lastMountedPosition = currentMountedPosition;
                    }
                }
                if (_hookHuman != null && !_hookHuman.Dead)
                {
                    Vector3 vector2 = _hookHuman.Cache.Transform.position - Cache.Transform.position;
                    float magnitude = vector2.magnitude;
                    // Temporarily remove until a rework is done as this completely breaks hook physics
                    /*if (magnitude > 2f)
                        Cache.Rigidbody.AddForce((vector2.normalized * Mathf.Pow(magnitude, 0.15f) * 30f) - (Cache.Rigidbody.velocity * 0.95f), ForceMode.VelocityChange);*/
                    _hookHumanConstantTimeLeft -= Time.fixedDeltaTime;
                    if (_hookHumanConstantTimeLeft <= 0f)
                    {
                        _hookHumanConstantTimeLeft = 1f;
                        _hookHuman.Cache.PhotonView.RPC("OnStillHookedByHuman", _hookHuman.Cache.PhotonView.Owner, new object[] { Cache.PhotonView.ViewID });
                    }
                }
                _currentVelocity = Cache.Rigidbody.velocity;
                GameProgressManager.RegisterSpeed(_currentVelocity.magnitude);
                CheckGround();

                float rotationSpeed = 6f;
                if (Grounded)
                {
                    rotationSpeed = 10f;
                }
                if (MountState == HumanMountState.None)
                {
                    Cache.Transform.rotation = Quaternion.Lerp(Cache.Transform.rotation, _targetRotation, Time.deltaTime * rotationSpeed);
                }
                bool pivotLeft = FixedUpdateLaunch(true);
                bool pivotRight = FixedUpdateLaunch(false);
                bool pivot = pivotLeft || pivotRight;
                if (Grounded)
                {
                    Vector3 newVelocity = Vector3.zero;
                    if (JustGrounded)
                    {
                        if (State != HumanState.Attack && State != HumanState.SpecialAttack && State != HumanState.SpecialAction
                            && State != HumanState.Stun && !HasDirection && !HasHook())
                        {
                            State = HumanState.Land;
                            CrossFade(HumanAnimations.Land, 0.01f);
                            if (!IsPlayingSound(HumanSounds.Land))
                                PlaySound(HumanSounds.Land);
                        }
                        else
                        {
                            _attackButtonRelease = true;
                            Vector3 v = _currentVelocity;
                            if (State != HumanState.Attack && State != HumanState.SpecialAttack && State != HumanState.SpecialAction && State != HumanState.Stun &&
                                State != HumanState.EmoteAction && (v.x * v.x + v.z * v.z > Stats.RunSpeed * Stats.RunSpeed * 1.5f) && State != HumanState.Refill)
                            {
                                State = HumanState.Slide;
                                CrossFade(HumanAnimations.Slide, 0.05f);
                                TargetAngle = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                                _targetRotation = GetTargetRotation();
                                HasDirection = true;
                                if (!IsPlayingSound(HumanSounds.CrashLand) && SettingsManager.SoundSettings.CrashLandEffect.Value)
                                    PlaySound(HumanSounds.CrashLand);
                            }
                        }
                        newVelocity = _currentVelocity;
                    }
                    if (State == HumanState.GroundDodge)
                    {
                        if (Animation.GetNormalizedTime(HumanAnimations.Dodge) >= 0.2f && Animation.GetNormalizedTime(HumanAnimations.Dodge) < 0.8f)
                            newVelocity = -Cache.Transform.forward * 2.4f * Stats.RunSpeed;
                        else if (Animation.GetNormalizedTime(HumanAnimations.Dodge) > 0.8f)
                            newVelocity = Cache.Rigidbody.velocity * 0.9f;
                    }
                    else if (State == HumanState.Idle)
                    {
                        newVelocity = Vector3.zero;
                        if (HasDirection)
                        {
                            newVelocity = GetTargetDirection() * TargetMagnitude * Stats.RunSpeed;
                            if (!Animation.IsPlaying(HumanAnimations.Run) && !Animation.IsPlaying(HumanAnimations.Jump) &&
                                !Animation.IsPlaying(HumanAnimations.RunBuffed) && (!Animation.IsPlaying(HumanAnimations.HorseMount) ||
                                Animation.GetNormalizedTime(HumanAnimations.HorseMount) >= 0.5f))
                            {
                                CrossFade(RunAnimation, 0.1f);
                                _stepPhase = 0;
                            }
                            if (!Animation.IsPlaying(HumanAnimations.WallRun))
                                _targetRotation = GetTargetRotation();
                        }
                        else if (!(Animation.IsPlaying(StandAnimation) || State == HumanState.Land || Animation.IsPlaying(HumanAnimations.Jump) || Animation.IsPlaying(HumanAnimations.HorseMount) || Animation.IsPlaying(HumanAnimations.Grabbed)))
                        {
                            CrossFade(StandAnimation, 0.1f);
                        }
                    }
                    else if (State == HumanState.Land)
                    {
                        newVelocity = Cache.Rigidbody.velocity * 0.96f;
                    }
                    else if (State == HumanState.Slide)
                    {
                        if (!_wallSlide)
                            newVelocity = Cache.Rigidbody.velocity * 0.985f;
                        if (_currentVelocity.magnitude < Stats.RunSpeed * 1.2f)
                        {
                            Idle();
                        }
                    }
                    Vector3 force = newVelocity - _currentVelocity;
                    force.x = Mathf.Clamp(force.x, -MaxVelocityChange, MaxVelocityChange);
                    force.z = Mathf.Clamp(force.z, -MaxVelocityChange, MaxVelocityChange);
                    force.y = 0f;
                    if (Animation.IsPlaying(HumanAnimations.Jump) && Animation.GetNormalizedTime(HumanAnimations.Jump) > 0.18f)
                    {
                        // float jumpSpeed = ((0.5f * (float)Stats.Speed) - 20f);
                        float jumpSpeed = 20f;
                        if (_currentVelocity.y > 0f)
                            jumpSpeed -= _currentVelocity.y;
                        force.y += Mathf.Max(jumpSpeed, 0f);
                    }
                    if (Animation.IsPlaying(HumanAnimations.HorseMount) && Animation.GetNormalizedTime(HumanAnimations.HorseMount) > 0.18f && Animation.GetNormalizedTime(HumanAnimations.HorseMount) < 1f)
                    {
                        force = -_currentVelocity;
                        force.y = 6f;
                        float distance = Vector3.Distance(Horse.Cache.Transform.position, Cache.Transform.position);
                        force += (Horse.Cache.Transform.position - Cache.Transform.position).normalized * 0.6f * Gravity.magnitude * distance / 12f;
                    }
                    if (!IsStock(pivot) && !pivot)
                    {
                        _currentVelocity += force;
                        Cache.Rigidbody.velocity = _currentVelocity;
                    }
                    Cache.Rigidbody.rotation = Quaternion.Lerp(Cache.Transform.rotation, Quaternion.Euler(0f, TargetAngle, 0f), Time.deltaTime * 10f);
                    ToggleSparks(State == HumanState.Slide);
                }
                else
                {
                    if (Horse != null && (Animation.IsPlaying(HumanAnimations.HorseMount) || Animation.IsPlaying(HumanAnimations.AirFall)) && Cache.Rigidbody.velocity.y < 0f && Vector3.Distance(Horse.Cache.Transform.position + Vector3.up * 1.65f, Cache.Transform.position) < 1f)
                    {
                        Cache.Transform.position = Horse.Cache.Transform.position + Vector3.up * 1.95f;
                        Cache.Transform.rotation = Horse.Cache.Transform.rotation;
                        MountState = HumanMountState.Horse;
                        MountedTransform = Horse.Cache.Transform;
                        CanMountedAttack = SettingsManager.InGameCurrent.Misc.HorsebackCombat.Value;
                        SetInterpolation(false);
                        if (!Animation.IsPlaying(HumanAnimations.HorseIdle))
                            CrossFade(HumanAnimations.HorseIdle, 0.1f);
                    }
                    else if (Animation.GetNormalizedTime(HumanAnimations.Dash) >= 0.99f || (State == HumanState.Idle && !Animation.IsPlaying(HumanAnimations.Dash) && !Animation.IsPlaying(HumanAnimations.WallRun) && !Animation.IsPlaying(HumanAnimations.ToRoof)
                        && !Animation.IsPlaying(HumanAnimations.HorseMount) && !Animation.IsPlaying(HumanAnimations.HorseDismount) && !Animation.IsPlaying(HumanAnimations.AirRelease)
                        && MountState == HumanMountState.None && (!Animation.IsPlaying(HumanAnimations.AirHookLJust) || Animation.GetNormalizedTime(HumanAnimations.AirHookLJust) >= 1f) && (!Animation.IsPlaying(HumanAnimations.AirHookRJust) || Animation.GetNormalizedTime(HumanAnimations.AirHookRJust) >= 1f)))
                    {
                        if (_wallSlide)
                        {
                            if (!Animation.IsPlaying(HumanAnimations.Slide))
                                CrossFade(HumanAnimations.Slide, 0.1f);
                        }
                        else if (!IsHookedAny() && (Animation.IsPlaying(HumanAnimations.AirHookL) || Animation.IsPlaying(HumanAnimations.AirHookR) || Animation.IsPlaying(HumanAnimations.AirHook)) && Cache.Rigidbody.velocity.y > 20f)
                        {
                            CrossFade(HumanAnimations.AirRelease);
                        }
                        else
                        {
                            if ((Mathf.Abs(_currentVelocity.x) + Mathf.Abs(_currentVelocity.z)) <= 25f)
                            {
                                if (_currentVelocity.y < 0f)
                                {
                                    if (!Animation.IsPlaying(HumanAnimations.AirFall))
                                        CrossFade(HumanAnimations.AirFall, 0.2f);
                                }
                                else if (!Animation.IsPlaying(HumanAnimations.AirRise))
                                    CrossFade(HumanAnimations.AirRise, 0.2f);
                            }
                            else if (!IsHookedAny())
                            {
                                float angle = -Mathf.DeltaAngle(-Mathf.Atan2(_currentVelocity.z, _currentVelocity.x) * Mathf.Rad2Deg, Cache.Transform.rotation.eulerAngles.y - 90f);
                                if (Mathf.Abs(angle) < 45f)
                                {
                                    if (!Animation.IsPlaying(HumanAnimations.Air2))
                                        CrossFade(HumanAnimations.Air2, 0.2f);
                                }
                                else if ((angle < 135f) && (angle > 0f))
                                {
                                    if (!Animation.IsPlaying(HumanAnimations.Air2Right))
                                        CrossFade(HumanAnimations.Air2Right, 0.2f);
                                }
                                else if ((angle > -135f) && (angle < 0f))
                                {
                                    if (!Animation.IsPlaying(HumanAnimations.Air2Left))
                                        CrossFade(HumanAnimations.Air2Left, 0.2f);
                                }
                                else if (!Animation.IsPlaying(HumanAnimations.Air2Backward))
                                    CrossFade(HumanAnimations.Air2Backward, 0.2f);
                            }
                            else if (Setup.Weapon == HumanWeapon.AHSS || Setup.Weapon == HumanWeapon.APG)
                            {
                                if (IsHookedLeft())
                                {
                                    if (!Animation.IsPlaying(HumanAnimations.AHSSHookForwardL))
                                        CrossFade(HumanAnimations.AHSSHookForwardL, 0.1f);
                                }
                                else if (IsHookedRight())
                                {
                                    if (!Animation.IsPlaying(HumanAnimations.AHSSHookForwardR))
                                        CrossFade(HumanAnimations.AHSSHookForwardR, 0.1f);
                                }
                                else if (!Animation.IsPlaying(HumanAnimations.AHSSHookForwardBoth))
                                    CrossFade(HumanAnimations.AHSSHookForwardBoth, 0.1f);
                            }
                            else if (!IsHookedRight())
                            {
                                if (!Animation.IsPlaying(HumanAnimations.AirHookL))
                                    CrossFade(HumanAnimations.AirHookL, 0.1f);
                            }
                            else if (!IsHookedLeft())
                            {
                                if (!Animation.IsPlaying(HumanAnimations.AirHookR))
                                    CrossFade(HumanAnimations.AirHookR, 0.1f);
                            }
                            else if (!Animation.IsPlaying(HumanAnimations.AirHook))
                                CrossFade(HumanAnimations.AirHook, 0.1f);
                        }
                    }
                    if (!Animation.IsPlaying(HumanAnimations.AirRise))
                    {
                        if (State == HumanState.Idle && Animation.IsPlaying(HumanAnimations.AirRelease) && Animation.GetNormalizedTime(HumanAnimations.AirRelease) >= 1f)
                            CrossFade(HumanAnimations.AirRise, 0.2f);
                        else if (Animation.IsPlaying(HumanAnimations.HorseDismount) && Animation.GetNormalizedTime(HumanAnimations.HorseDismount) >= 1f)
                            CrossFade(HumanAnimations.AirRise, 0.2f);
                    }
                    if (Animation.IsPlaying(HumanAnimations.ToRoof))
                    {
                        if (Animation.GetNormalizedTime(HumanAnimations.ToRoof) < 0.22f)
                        {
                            Cache.Rigidbody.velocity = Vector3.zero;
                            Cache.Rigidbody.AddForce(new Vector3(0f, Gravity.magnitude * Cache.Rigidbody.mass, 0f));
                        }
                        else
                        {
                            if (!_wallJump)
                            {
                                _wallJump = true;
                                Cache.Rigidbody.AddForce(Vector3.up * 8f, ForceMode.Impulse);
                            }
                            Cache.Rigidbody.AddForce(Cache.Transform.forward * 0.05f, ForceMode.Impulse);
                        }
                        if (Animation.GetNormalizedTime(HumanAnimations.ToRoof) >= 1f)
                        {
                            PlayAnimation(HumanAnimations.AirRise);
                        }
                    }
                    else if (!(State != HumanState.Idle || !IsPressDirectionTowardsHero() || SettingsManager.InputSettings.Human.Jump.GetKey() || SettingsManager.InputSettings.Human.HookLeft.GetKey() || SettingsManager.InputSettings.Human.HookRight.GetKey() || SettingsManager.InputSettings.Human.HookBoth.GetKey() || !IsFrontGrounded() || Animation.IsPlaying(HumanAnimations.WallRun) || Animation.IsPlaying(HumanAnimations.Dodge)))
                    {
                        CrossFade(HumanAnimations.WallRun, 0.1f);
                        _wallRunTime = 0f;
                    }
                    else if (Animation.IsPlaying(HumanAnimations.WallRun))
                    {
                        Cache.Rigidbody.AddForce(Vector3.up * Stats.RunSpeed - Cache.Rigidbody.velocity, ForceMode.VelocityChange);
                        _wallRunTime += Time.deltaTime;
                        if (!HasDirection)
                        {
                            Cache.Rigidbody.AddForce(-Cache.Transform.forward * Stats.RunSpeed * 0.75f, ForceMode.Impulse);
                            DodgeWall();
                        }
                        else if (!IsUpFrontGrounded())
                        {
                            _wallJump = false;
                            CrossFade(HumanAnimations.ToRoof, 0.1f);
                        }
                        else if (!IsFrontGrounded())
                            CrossFade(HumanAnimations.AirFall, 0.1f);
                    }
                    else if (!Animation.IsPlaying(HumanAnimations.Dash) && !Animation.IsPlaying(HumanAnimations.Jump) && !IsFiringThunderspear())
                    {
                        Vector3 targetDirection = GetTargetDirection() * TargetMagnitude * ((float)Stats.Acceleration * 2f - 50f) / 5f;
                        if (!HasDirection)
                        {
                            if (State == HumanState.Attack)
                                targetDirection = Vector3.zero;
                        }
                        else
                            _targetRotation = GetTargetRotation();
                        bool isUsingGas = SettingsManager.InputSettings.Human.Jump.GetKey() ^ SettingsManager.InputSettings.Human.AutoUseGas.Value;
                        if (((!pivotLeft && !pivotRight) && (MountState == HumanMountState.None && isUsingGas)) && (Stats.CurrentGas > 0f))
                        {
                            if (HasDirection)
                            {
                                Cache.Rigidbody.AddForce(targetDirection, ForceMode.Acceleration);
                            }
                            else
                            {
                                Cache.Rigidbody.AddForce((Cache.Transform.forward * targetDirection.magnitude), ForceMode.Acceleration);
                            }
                            pivot = true;
                        }
                    }
                    if ((Animation.IsPlaying(HumanAnimations.AirFall) && (_currentVelocity.magnitude < 0.2f)) && this.IsFrontGrounded())
                    {
                        CrossFade(HumanAnimations.OnWall, 0.3f);
                    }
                    FixedUpdateWallSlide();
                }
                if (pivotLeft && pivotRight)
                    FixedUpdatePivot((HookRight.GetHookPosition() + HookLeft.GetHookPosition()) * 0.5f);
                else if (pivotLeft)
                    FixedUpdatePivot(HookLeft.GetHookPosition());
                else if (pivotRight)
                    FixedUpdatePivot(HookRight.GetHookPosition());
                bool lowerGravity = false;
                if (IsHookedLeft() && HookLeft.GetHookPosition().y > Cache.Transform.position.y && _launchLeft)
                    lowerGravity = true;
                else if (IsHookedRight() && HookRight.GetHookPosition().y > Cache.Transform.position.y && _launchRight)
                    lowerGravity = true;
                Vector3 gravity;
                if (lowerGravity)
                    gravity = Gravity * 0.5f * Cache.Rigidbody.mass;
                else
                    gravity = Gravity * Cache.Rigidbody.mass;
                if (Grounded && State == HumanState.Attack)
                {
                    if (ValidStockAttacks())
                    {
                        bool stockPivot = pivotLeft || pivotRight;
                        bool isStock = IsStock(stockPivot);
                        if (isStock && CanStockDueToBL() || !stockPivot && CanStockDueToBL())
                        {
                            _currentVelocity += Cache.Transform.forward * 4f / Mathf.Max(Cache.Rigidbody.mass, 0.001f);
                            Cache.Rigidbody.velocity = _currentVelocity;
                        }
                        if (!SettingsManager.InGameCurrent.Misc.AllowStock.Value || SettingsManager.InGameCurrent.Misc.RealismMode.Value)
                        {
                            _currentVelocity = _currentVelocity.normalized * Mathf.Min(_currentVelocity.magnitude, 20f);
                            Cache.Rigidbody.velocity = _currentVelocity;
                        }
                    }
                    ToggleSparks(false);
                }
                gravity += WeatherManager.GetWeatherForce();
                Cache.Rigidbody.AddForce(gravity);
                if (!_cancelGasDisable)
                {
                    if (pivot)
                    {
                        Stats.UseFrameGas();
                        if (!HumanCache.Smoke.emission.enabled)
                            Cache.PhotonView.RPC("SetSmokeRPC", RpcTarget.All, new object[] { true });
                        if (!IsPlayingSound(HumanSounds.GasLoop) && SettingsManager.SoundSettings.GasEffect.Value)
                            PlaySound(HumanSounds.GasLoop);
                    }
                    else
                    {
                        if (HumanCache.Smoke.emission.enabled)
                            Cache.PhotonView.RPC("SetSmokeRPC", RpcTarget.All, new object[] { false });
                        if (IsPlayingSound(HumanSounds.GasLoop))
                        {
                            StopSound(HumanSounds.GasLoop);
                            ToggleSound(HumanSounds.GasEnd, true);
                        }
                    }
                }
                else
                    _cancelGasDisable = false;
                var windEmission = HumanCache.Wind.emission;
                var windMain = HumanCache.Wind.main;
                if (WindWeatherEffect.WindEnabled)
                {
                    if (!windEmission.enabled)
                        windEmission.enabled = true;
                    windMain.startSpeedMultiplier = 100f;
                    HumanCache.WindTransform.LookAt(Cache.Transform.position + WindWeatherEffect.WindDirection);
                }
                else if (_currentVelocity.magnitude > 80f && SettingsManager.GraphicsSettings.WindEffectEnabled.Value)
                {
                    if (!windEmission.enabled)
                        windEmission.enabled = true;
                    windMain.startSpeedMultiplier = _currentVelocity.magnitude;
                    HumanCache.WindTransform.LookAt(Cache.Transform.position - _currentVelocity);
                }
                else if (windEmission.enabled)
                    windEmission.enabled = false;
                FixedUpdateSetHookedDirection();
                FixedUpdateBodyLean();
                if (_useFixedUpdateClipping)
                {
                    FixedUpdateClippingCheck();
                }
                else
                {
                    _useFixedUpdateClipping = true;
                    _lastPosition = Cache.Rigidbody.position;
                    _lastVelocity = _currentVelocity;

                }

                ReelInAxis = 0f;
            }
            EnableSmartTitans();
        }
        private bool CanStockDueToBL()
        {
            if (IsHookedLeft() && IsHookedRight())
            {
                if (_almostSingleHook)
                {
                    return false;
                }
            }
            else if (IsHookedLeft())
            {
                return false;
            }
            else if (IsHookedRight())
            {
                return false;
            }

            return true;
        }
        private bool ValidStockAttacks()
        {
            return Animation.IsPlaying(HumanAnimations.Attack1) || Animation.IsPlaying(HumanAnimations.Attack2)
                || Animation.IsPlaying(HumanAnimations.Attack1HookL1) || Animation.IsPlaying(HumanAnimations.Attack1HookR1)
                || Animation.IsPlaying(HumanAnimations.Attack1HookL2) || Animation.IsPlaying(HumanAnimations.Attack1HookR2);
        }

        public bool HasGrabImmunity()
        {
            return _grabIFrames > 0;
        }

        public void StartGrabImmunity(float duration)
        {
            _grabIFrames = duration;
        }

        private void UpdateIFrames()
        {
            InvincibleTimeLeft -= Time.deltaTime;
            if (InvincibleTimeLeft <= 0f)
                IsInvincible = false;
            if (_grabIFrames > 0)
                _grabIFrames -= Time.deltaTime;
        }

        private void UpdateBladeFire()
        {
            if (Setup == null || Setup.Weapon != HumanWeapon.Blade)
                return;
            int rank = ((InGameMenu)UIManager.CurrentMenu).GetStylebarRank();
            if (rank >= 6)
                ToggleBladeFire(2);
            else if (rank >= 4)
                ToggleBladeFire(1);
            else
                ToggleBladeFire(0);
        }

        private void lookAtTarget(Vector3 target)
        {
            Transform chestT = HumanCache.Chest;
            Transform spineT = HumanCache.Spine;

            float dx = target.x - base.transform.position.x;
            float dz = target.z - base.transform.position.z;
            float xAngle = Mathf.Sqrt(dx * dx + dz * dz);

            HumanCache.Head.rotation = chestT.rotation;
            Vector3 targetRay = target - base.transform.position;
            float yaw = 0f - Mathf.DeltaAngle((0f - Mathf.Atan2(targetRay.z, targetRay.x)) * 57.29578f, base.transform.rotation.eulerAngles.y - 90f);
            float pitch = Mathf.Atan2(spineT.position.y - target.y, yaw) * 57.29578f;

            yaw = Mathf.Clamp(yaw, -40f, 40f);
            pitch = Mathf.Clamp(pitch, -40f, 30f);

            HumanCache.Head.rotation = Quaternion.Euler(chestT.rotation.eulerAngles.x + pitch, chestT.rotation.eulerAngles.y + yaw, chestT.rotation.eulerAngles.z);
            HumanCache.Head.localRotation = Quaternion.Lerp(_oldHeadRotation, HumanCache.Head.localRotation, Time.deltaTime * 12);
            chestT.rotation = HumanCache.Head.localRotation;
        }

        protected void LateUpdateHeadPosition(Vector3 position)
        {
            if (position != null)
            {
                Vector3 vector = position - Cache.Transform.position;
                Vector2 angle = GetLookAngle(position);

                // maintain horizontal angle if within buffer zone on left or right.
                bool isInLeftRange = angle.y > -120 && angle.y < -50;
                bool isInRightRange = angle.y > 50 && angle.y < 120;

                if (isInLeftRange || isInRightRange)
                {
                    // if we were in front of the character before hitting the buffer zone, use the camera for the yaw, otherwise it will use the the ray.
                    if (LastGoodHeadAngle.y < -50 || LastGoodHeadAngle.y > 50)
                    {
                        // set angle to look at the camera
                        position = SceneLoader.CurrentCamera.Camera.transform.position;
                        angle = GetLookAngle(position);
                    }

                    angle.y = LastGoodHeadAngle.y;
                    LastGoodHeadAngle.x = angle.x;
                }
                else if (Vector3.Dot(Cache.Transform.forward, vector.normalized) < 0)
                {
                    // set angle to look at the camera
                    position = SceneLoader.CurrentCamera.Camera.transform.position;
                    angle = GetLookAngle(position);
                    LastGoodHeadAngle = angle;
                }
                else
                {
                    LastGoodHeadAngle = angle;
                }

                angle.x = Mathf.Clamp(angle.x, -80f, 30f);
                angle.y = Mathf.Clamp(angle.y, -80f, 80f);

                HumanCache.Head.rotation = Quaternion.Euler(HumanCache.Head.rotation.eulerAngles.x - angle.x,
                    HumanCache.Head.rotation.eulerAngles.y + angle.y, HumanCache.Head.rotation.eulerAngles.z);
                HumanCache.Head.localRotation = Quaternion.Lerp(_oldHeadRotation, HumanCache.Head.localRotation, Time.deltaTime * 10f);
            }
            else
            {
                HumanCache.Head.localRotation = Quaternion.Lerp(_oldHeadRotation, HumanCache.Head.localRotation, Time.deltaTime * 10f);
                LastGoodHeadAngle = Vector2.zero;
            }
            _oldHeadRotation = HumanCache.Head.localRotation;

            LateUpdateHeadRotation = HumanCache.Head.rotation;

        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (IsMine() && State != HumanState.Grab)
            {
                if (MountState == HumanMountState.None)
                {
                    LateUpdateTilt();
                    LateUpdateReelOut();
                }
                if (IsAttackableState)
                {
                    LateUpdateGun();
                }
                bool validState = State == HumanState.Idle || State == HumanState.Run || State == HumanState.Slide;
                if (Grounded && validState && !_cameraFPS)
                {
                    var aimPoint = GetAimPoint();
                    LateUpdateHeadPosition(aimPoint);
                }
                else
                {
                    LastGoodHeadAngle = Vector2.zero;
                    LateUpdateHeadRotation = null;
                    _oldHeadRotation = HumanCache.Head.localRotation;
                }
            }
            else if (!IsMine())
            {
                if (LateUpdateHeadRotationRecv != null)
                {
                    HumanCache.Head.rotation = (Quaternion)LateUpdateHeadRotationRecv;
                    HumanCache.Head.localRotation = Quaternion.Lerp(_oldHeadRotation, HumanCache.Head.localRotation, Time.deltaTime * 10f);
                    _oldHeadRotation = HumanCache.Head.localRotation;
                }
            }
        }

        protected override void LateUpdateFPS()
        {
            if (!IsMine() || !FinishSetup || !_customSkinLoader.Finished)
                return;
            var camera = ((InGameCamera)SceneLoader.CurrentCamera);
            if (camera._follow == this && camera.GetCameraDistance() == 0f)
            {
                if (!_cameraFPS)
                {
                    _cameraFPS = true;
                    foreach (var renderer in GetFPSDisabledRenderers())
                    {
                        FPSMaterials[renderer] = renderer.material;
                        renderer.material = MaterialCache.TransparentMaterial;
                    }
                }
            }
            else if (_cameraFPS)
            {
                _cameraFPS = false;
                if (!Dead || !(this is Human))
                {
                    foreach (var renderer in GetFPSDisabledRenderers())
                    {
                        renderer.material = FPSMaterials[renderer];
                    }
                }
            }
        }

        protected void OnCollisionEnter(Collision collision)
        {
            if (!IsMine())
                return;
            var velocity = Cache.Rigidbody.velocity;
            if (Special != null && Special is SwitchbackSpecial)
            {
                if (((SwitchbackSpecial)Special).RegisterCollision(this, collision, _lastVelocity.magnitude * 0.7f))
                    return;
            }
            if (_lastVelocity.magnitude > 0f)
            {
                float angle = Mathf.Abs(Vector3.Angle(velocity, _lastVelocity));
                float speedMultiplier = Mathf.Max(1f - (angle * 1.5f * 0.01f), 0f);
                float speed = _lastVelocity.magnitude * speedMultiplier;
                Cache.Rigidbody.velocity = velocity.normalized * speed;
                float speedDiff = _lastVelocity.magnitude - Cache.Rigidbody.velocity.magnitude;
                if (SettingsManager.InGameCurrent.Misc.RealismMode.Value && speedDiff > RealismDeathVelocity)
                    GetHit("Impact", (int)speedDiff, "Impact", "");
            }
            var titan = collision.transform.root.GetComponent<BaseTitan>();
            if (titan != null && !titan.AI)
            {
                var normal = collision.contacts[0].normal;
                var titanVel = titan.GetVelocity();
                if (titanVel.magnitude > 0f && Vector3.Angle(titanVel, normal) < 70f)
                {
                    Cache.Rigidbody.velocity += titanVel;
                }
            }
        }

        protected void OnCollisionStay(Collision collision)
        {
            if (!Grounded && Cache.Rigidbody.velocity.magnitude >= 15f && !Animation.IsPlaying(HumanAnimations.WallRun) && collision.gameObject.layer != PhysicsLayer.MapObjectTitans)
            {
                if (SettingsManager.InputSettings.Human.WallSlideAttach.Value == (int)WallSlideAttachMethod.Auto ||
                    (SettingsManager.InputSettings.Human.WallSlideAttach.Value == (int)WallSlideAttachMethod.Strafe
                    && IsPressDirectionRelativeToWall(-collision.GetContact(0).normal.normalized, 0.5f)))
                {
                    _wallSlide = true;
                    _wallSlideGround = collision.GetContact(0).normal.normalized;
                }
            }
            if (Special != null && Special is SwitchbackSpecial)
            {
                ((SwitchbackSpecial)Special).RegisterCollision(this, collision, Cache.Rigidbody.velocity.magnitude);
            }
        }

        private void FixedUpdateWallSlide()
        {

            if (_wallSlide)
            {
                if (!_canWallSlideJump && !IsPressDirectionRelativeToWall(_wallSlideGround, 0.5f))
                    if (SettingsManager.InputSettings.Human.WallSlideDash.Value)
                    {
                        _canWallSlideJump = true;
                    }


                if (Grounded)
                {
                    EndWallSlide();
                }

                else if (Cache.Rigidbody.velocity.magnitude < 15f)
                {
                    EndWallSlide();
                }
                else if (!CheckRaycastIgnoreTriggers(Cache.Transform.position + Vector3.up * 0.7f, -_wallSlideGround, 1f, GroundMask.value))
                {
                    EndWallSlide();
                }
                else if (IsPressDirectionRelativeToWall(_wallSlideGround, 0.5f) && _canWallSlideJump) //pressing away from the wall
                {
                    Cache.Rigidbody.AddForce(_wallSlideGround * Stats.RunSpeed * 0.75f, ForceMode.Impulse);
                    DodgeWall();
                }
                else if (IsPressDirectionRelativeToWall(-_wallSlideGround, 0.8f)) //pressing towards the wall
                {
                    EndWallSlide();
                }
            }
            ToggleSparks(_wallSlide);
        }

        private void EndWallSlide()
        {
            _wallSlide = false;
            _canWallSlideJump = false;
        }

        private void LateUpdateReelOut()
        {
            ToggleSound(HumanSounds.ReelOut, _isReelingOut && SettingsManager.SoundSettings.ReelOutEffect.Value);
        }

        private bool FixedUpdateLaunch(bool left)
        {
            bool launch;
            HookUseable hook;
            bool pivot = false;
            float launchTime;
            if (left)
            {
                launch = _launchLeft;
                hook = HookLeft;
                _launchLeftTime += Time.deltaTime;
                launchTime = _launchLeftTime;
            }
            else
            {
                launch = _launchRight;
                hook = HookRight;
                _launchRightTime += Time.deltaTime;
                launchTime = _launchRightTime;
            }
            if (launch)
            {
                if (hook.IsHooked())
                {
                    Vector3 v = (hook.GetHookPosition() - Cache.Transform.position).normalized * 10f;
                    if (!(_launchLeft && _launchRight))
                        v *= 2f;
                    if ((Vector3.Angle(Cache.Rigidbody.velocity, v) > 90f) && (SettingsManager.InputSettings.Human.Jump.GetKey() ^ SettingsManager.InputSettings.Human.AutoUseGas.Value))
                    {
                        pivot = true;
                    }
                    if (!pivot)
                    {
                        Cache.Rigidbody.AddForce(v);
                        if (Vector3.Angle(Cache.Rigidbody.velocity, v) > 90f)
                            Cache.Rigidbody.AddForce(-Cache.Rigidbody.velocity * 2f, ForceMode.Acceleration);
                    }
                }
                if (hook.IsActive && Stats.CurrentGas > 0f)
                    Stats.UseFrameGas();
                else if (launchTime > 0.3f)
                {
                    if (left)
                        _launchLeft = false;
                    else
                        _launchRight = false;
                    hook.DisableActiveHook();
                    UnhookHuman(left);
                    pivot = false;
                }
            }
            return pivot;
        }

        private void FixedUpdatePivot(Vector3 position)
        {
            float addSpeed = 0.1f;
            if (Grounded)
                addSpeed = -0.01f;
            float newSpeed = _currentVelocity.magnitude + addSpeed;

            Vector3 v = position - Cache.Rigidbody.position;
            if (IsHookedLeft() && IsHookedRight())
            {
                if (HookLeft.IsHookOffset() && HookRight.IsHookOffset())
                {
                    v = position - (Cache.Rigidbody.position - new Vector3(0, 0.020f, 0)); // 0.020F gives the player the original aottg1 clipping required for bounce.
                }
            }

            float reelAxis = GetReelAxis();
            if (reelAxis > 0f)
            {
                if (SettingsManager.InGameCurrent.Misc.RealismMode.Value && Vector3.Distance(Cache.Transform.position, position) > RealismMaxReel)
                    reelAxis = 0f;
            }
            float reel = Mathf.Clamp(reelAxis, -0.8f, 0.8f) + 1f;
            v = Vector3.RotateTowards(v, _currentVelocity, 1.53938f * reel, 1.53938f * reel).normalized;
            if (reelAxis > 0f)
                _isReelingOut = true;
            else if (reelAxis < 0f && !_reelInWaitForRelease)
            {
                if (State == HumanState.AirDodge && IsHookedLeft() && IsHookedRight())
                    return;
                if (SettingsManager.SoundSettings.ReelInEffect.Value)
                    PlaySoundRPC(HumanSounds.ReelIn, Util.CreateLocalPhotonInfo());
                if (!SettingsManager.InputSettings.Human.ReelInHolding.Value)
                    _reelInWaitForRelease = true;
            }
            _currentVelocity = v * newSpeed;
            Cache.Rigidbody.velocity = _currentVelocity;
        }

        private bool IsStock(bool pivot)
        {
            return Grounded && State == HumanState.Attack && pivot && ValidStockAttacks();
        }

        private void FixedUpdateSetHookedDirection()
        {
            _almostSingleHook = false;
            float oldTargetAngle = TargetAngle;
            if (IsHookedLeft() && IsHookedRight())
            {
                Vector3 hookDiff = HookLeft.GetHookPosition() - HookRight.GetHookPosition();
                Vector3 direction = (HookLeft.GetHookPosition() + HookRight.GetHookPosition()) * 0.5f - Cache.Transform.position;
                if (hookDiff.sqrMagnitude < 4f)
                {
                    TargetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                    if ((Setup.Weapon == HumanWeapon.AHSS || Setup.Weapon == HumanWeapon.APG) && State != HumanState.Attack)
                    {
                        float current = -Mathf.Atan2(Cache.Rigidbody.velocity.z, Cache.Rigidbody.velocity.x) * Mathf.Rad2Deg;
                        float target = -Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg;
                        TargetAngle -= Mathf.DeltaAngle(current, target);
                    }
                    _almostSingleHook = true;
                }
                else
                {
                    Vector3 left = Cache.Transform.position - HookLeft.GetHookPosition();
                    Vector3 right = Cache.Transform.position - HookRight.GetHookPosition();
                    if (Vector3.Angle(-direction, left) < 30f && Vector3.Angle(-direction, right) < 30f)
                    {
                        _almostSingleHook = true;
                        TargetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                    }
                    else
                    {
                        _almostSingleHook = false;
                        Vector3 forward = Cache.Transform.forward;
                        Vector3.OrthoNormalize(ref hookDiff, ref forward);
                        TargetAngle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                        float angle = Mathf.Atan2(left.x, left.z) * Mathf.Rad2Deg;
                        if (Mathf.DeltaAngle(angle, TargetAngle) > 0f)
                            TargetAngle += 180f;
                    }
                }
            }
            else
            {
                _almostSingleHook = true;
                Vector3 v;
                if (IsHookedLeft())
                    v = HookLeft.GetHookPosition() - Cache.Transform.position;
                else if (IsHookedRight())
                    v = HookRight.GetHookPosition() - Cache.Transform.position;
                else
                    return;
                TargetAngle = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                if (State != HumanState.Attack)
                {
                    float angle1 = -Mathf.Atan2(Cache.Rigidbody.velocity.z, Cache.Rigidbody.velocity.x) * Mathf.Rad2Deg;
                    float angle2 = -Mathf.Atan2(v.z, v.x) * Mathf.Rad2Deg;
                    float delta = -Mathf.DeltaAngle(angle1, angle2);
                    if (Setup.Weapon == HumanWeapon.AHSS || Setup.Weapon == HumanWeapon.APG)
                        TargetAngle += delta;
                    else
                    {
                        float multiplier = 0.1f;
                        if ((IsHookedLeft() && delta < 0f) || (IsHookedRight() && delta > 0f))
                            multiplier = -0.1f;
                        TargetAngle += delta * multiplier;
                    }
                }
            }
            if (IsFiringThunderspear())
                TargetAngle = oldTargetAngle;
            if (Grounded && HasDirection && State != HumanState.Attack && State != HumanState.Slide)
                TargetAngle = oldTargetAngle;
        }

        private void FixedUpdateBodyLean()
        {
            float z = 0f;
            _needLean = false;
            if (Setup.Weapon != HumanWeapon.AHSS && Setup.Weapon != HumanWeapon.APG && State == HumanState.Attack && !IsFiringThunderspear())
            {
                Vector3 v = Cache.Rigidbody.velocity;
                float diag = Mathf.Sqrt((v.x * v.x) + (v.z * v.z));
                float angle = Mathf.Atan2(v.y, diag) * Mathf.Rad2Deg;
                _targetRotation = Quaternion.Euler(-angle * (1f - (Vector3.Angle(v, Cache.Transform.forward) / 90f)), TargetAngle, 0f);
                if (IsHookedAny())
                    Cache.Transform.rotation = _targetRotation;
            }
            else
            {
                if (!Grounded)
                {
                    if (IsHookedLeft() && IsHookedRight())
                    {
                        if (_almostSingleHook)
                        {
                            _needLean = true;
                            z = GetLeanAngle(HookRight.GetHookPosition(), true);
                        }
                    }
                    else if (IsHookedLeft())
                    {
                        _needLean = true;
                        z = GetLeanAngle(HookLeft.GetHookPosition(), true);
                    }
                    else if (IsHookedRight())
                    {
                        _needLean = true;
                        z = GetLeanAngle(HookRight.GetHookPosition(), false);

                    }
                }
                if (_needLean)
                {
                    float a = 0f;
                    if (Setup.Weapon != HumanWeapon.AHSS && Setup.Weapon != HumanWeapon.APG && State != HumanState.Attack)
                    {
                        a = Cache.Rigidbody.velocity.magnitude * 0.1f;
                        a = Mathf.Min(a, 20f);
                    }
                    _targetRotation = Quaternion.Euler(-a, TargetAngle, z);
                }
                else if (State != HumanState.Attack && !Animation.IsPlaying(HumanAnimations.WallRun))
                    _targetRotation = Quaternion.Euler(0f, TargetAngle, 0f);
                if (_wallSlide && !Grounded)
                {
                    _targetRotation = Quaternion.LookRotation(Cache.Rigidbody.velocity, _wallSlideGround);
                }
            }
        }

        private void FixedUpdateUseables()
        {
            if (FinishSetup)
            {
                Weapon.OnFixedUpdate();
                HookLeft.OnFixedUpdate();
                HookRight.OnFixedUpdate();
                if (Special != null)
                    Special.OnFixedUpdate();
            }
        }

        public void FixedUpdateLookTitan()
        {
            Ray ray = SceneLoader.CurrentCamera.Camera.ScreenPointToRay(CursorManager.GetInGameMousePosition());
            LayerMask mask = TitanDetectionMask;
            RaycastHit[] hitArr = Physics.RaycastAll(ray, 200f, mask.value);
            if (hitArr.Length == 0)
                return;
            List<RaycastHit> hitList = new List<RaycastHit>(hitArr);
            hitList.Sort((x, y) => x.distance.CompareTo(y.distance));
            int maxCount = Math.Min(hitList.Count, 3);
            for (int i = 0; i < maxCount; i++)
            {
                var entity = hitList[i].collider.GetComponent<TitanProjectileDetection>();
                entity.Owner.TitanColliderToggler.RegisterLook();
            }
        }

        private void FixedUpdateClippingCheck()
        {
            Vector3 finalPosition = Cache.Rigidbody.position;
            if (_lastVelocity.magnitude > 100f)
            {
                float maxDistance = _lastVelocity.magnitude * 1.1f;
                Vector3 start = _lastPosition + Vector3.up * 0.7f;
                Vector3 end = Cache.Rigidbody.position + Vector3.up * 0.7f;
                Vector3 v = start - end;
                if (v.magnitude > maxDistance)
                    start = end + v.normalized * maxDistance;
                v = end - start;
                var hitArr = Physics.RaycastAll(start, v.normalized, v.magnitude, ClipMask.value);
                System.Array.Sort(hitArr, (x, y) => x.distance.CompareTo(y.distance));
                if (hitArr.Length > 0)
                {
                    bool foundHit = false;
                    RaycastHit firstHit = hitArr[0];
                    foreach (RaycastHit hit in hitArr)
                    {
                        if (hit.collider.isTrigger)
                        {
                            var collisionHandler = hit.collider.GetComponent<CustomLogicCollisionHandler>();
                            if (collisionHandler != null)
                                collisionHandler.OnTriggerEnter(GetComponent<CapsuleCollider>());
                            continue;
                        }
                        if (!foundHit)
                        {
                            firstHit = hit;
                            foundHit = true;
                        }
                    }
                    if (foundHit)
                    {
                        Vector3 position = firstHit.point - Vector3.up * 0.7f;
                        Cache.Rigidbody.position = position;
                        finalPosition = position;
                    }
                }
            }
            _lastPosition = finalPosition;
            _lastVelocity = _currentVelocity;
        }

        private void LateUpdateTilt()
        {
            if (IsMainCharacter() && SettingsManager.GeneralSettings.CameraTilt.Value)
            {
                Quaternion rotation;
                Vector3 left = Vector3.zero;
                Vector3 right = Vector3.zero;
                if (_launchLeft && IsHookedLeft())
                    left = HookLeft.GetHookPosition();
                if (_launchRight && IsHookedRight())
                    right = HookRight.GetHookPosition();
                Vector3 target = Vector3.zero;
                if (left.magnitude != 0f && right.magnitude == 0f)
                    target = left;
                else if (right.magnitude != 0f && left.magnitude == 0f)
                    target = right;
                else if (left.magnitude != 0f && right.magnitude != 0f)
                    target = 0.5f * (left + right);
                Transform camera = SceneLoader.CurrentCamera.Cache.Transform;
                Vector3 projectUp = Vector3.Project(target - Cache.Transform.position, camera.up);
                Vector3 projectRight = Vector3.Project(target - Cache.Transform.position, camera.right);
                if (target.magnitude > 0f)
                {
                    Vector3 projectDirection = projectUp + projectRight;
                    float angle = Vector3.Angle(target - Cache.Transform.position, Cache.Rigidbody.velocity) * 0.005f;
                    Vector3 finalRight = camera.right + projectRight.normalized;
                    float finalAngle = Vector3.Angle(projectUp, projectDirection) * angle;
                    rotation = Quaternion.Euler(camera.rotation.eulerAngles.x, camera.rotation.eulerAngles.y, (finalRight.magnitude >= 1f) ? -finalAngle : finalAngle);
                }
                else
                    rotation = Quaternion.Euler(camera.rotation.eulerAngles.x, camera.rotation.eulerAngles.y, 0f);
                camera.rotation = Quaternion.Lerp(camera.rotation, rotation, Time.deltaTime * 2f);
            }
        }

        private void LateUpdateGun()
        {
            if (Setup.Weapon == HumanWeapon.AHSS || Setup.Weapon == HumanWeapon.APG)
            {
                if (!Grounded)
                {
                    HumanCache.HandL.localRotation = Quaternion.Euler(0f, 0f, 0f);
                    HumanCache.HandR.localRotation = Quaternion.Euler(0f, 0f, 0f);
                }
                if (_gunArmAim && Setup.Weapon == HumanWeapon.AHSS)
                {
                    Vector3 target = GetAimPoint();
                    Vector3 direction = (target - Cache.Transform.position).normalized;
                    float angle = -Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg;
                    float delta = -Mathf.DeltaAngle(angle, Cache.Transform.rotation.eulerAngles.y - 90f);
                    GunHeadMovement();
                    if (!IsHookedAny())
                    {
                        if (delta <= 0f && delta > -90f)
                            LeftArmAim(target);
                        else if (delta > 0f && delta < 90f)
                            RightArmAim(target);
                    }
                    else if (!IsHookedLeft() && delta < 40f && delta > -90f)
                        LeftArmAim(target);
                    else if (!IsHookedRight() && delta > -40f && delta < 90f)
                        RightArmAim(target);
                }
                if (IsHookedLeft())
                    LeftArmAim(HookLeft.GetHookPosition());
                if (IsHookedRight())
                    RightArmAim(HookRight.GetHookPosition());
            }
        }

        private void GunHeadMovement()
        {
            return;
            Vector3 _gunTarget = GetAimPoint();
            Vector3 position = Cache.Transform.position;
            float x = Mathf.Sqrt(Mathf.Pow(_gunTarget.x - position.x, 2f) + Mathf.Pow(_gunTarget.z - position.z, 2f));
            var originalRotation = Cache.Transform.rotation;
            Vector3 euler = originalRotation.eulerAngles;
            Vector3 direction = _gunTarget - position;
            float angle = -Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg;
            float deltaY = -Mathf.DeltaAngle(angle, euler.y - 90f);
            deltaY = Mathf.Clamp(deltaY, -40f, 40f);
            float y = HumanCache.Neck.position.y - _gunTarget.y;
            float deltaX = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            deltaX = Mathf.Clamp(deltaX, -40f, 30f);
            var targetRotation = Quaternion.Euler(euler.x + deltaX, euler.y + deltaY, euler.z);
            _targetRotation = Quaternion.Lerp(_targetRotation, targetRotation, Time.deltaTime * 100f);
        }

        private void LeftArmAim(Vector3 target)
        {
            float x = target.x - HumanCache.UpperarmL.position.x;
            float y = target.y - HumanCache.UpperarmL.position.y;
            float z = target.z - HumanCache.UpperarmL.position.z;
            float sq = Mathf.Sqrt((x * x) + (z * z));
            HumanCache.HandL.localRotation = Quaternion.Euler(0f, 0f, 0f);
            HumanCache.ForearmL.localRotation = Quaternion.Euler(0f, 0f, 0f);
            HumanCache.UpperarmL.rotation = Quaternion.Euler(0f, -90f + (Mathf.Atan2(x, z) * Mathf.Rad2Deg), -90f + Mathf.Atan2(y, sq) * Mathf.Rad2Deg);
        }

        private void RightArmAim(Vector3 target)
        {
            float x = target.x - HumanCache.UpperarmR.position.x;
            float y = target.y - HumanCache.UpperarmR.position.y;
            float z = target.z - HumanCache.UpperarmR.position.z;
            float sq = Mathf.Sqrt((x * x) + (z * z));
            HumanCache.HandR.localRotation = Quaternion.Euler(0f, 0f, 0f);
            HumanCache.ForearmR.localRotation = Quaternion.Euler(0f, 0f, 0f);
            HumanCache.UpperarmR.rotation = Quaternion.Euler(180f, -90f + (Mathf.Atan2(x, z) * Mathf.Rad2Deg), -90f - Mathf.Atan2(y, sq) * Mathf.Rad2Deg);
        }

        protected override void SetColliders()
        {
            foreach (Collider c in GetComponentsInChildren<Collider>())
            {
                if (c.name == "checkBox")
                    c.gameObject.layer = PhysicsLayer.Hitbox;
                else
                    c.gameObject.layer = PhysicsLayer.NoCollision;
            }
            gameObject.layer = PhysicsLayer.Human;
        }

        [PunRPC]
        public void SetupRPC(string customSetJson, int humanWeapon, PhotonMessageInfo info)
        {
            if (info.Sender != Cache.PhotonView.Owner)
                return;
            HumanCustomSet set = new HumanCustomSet();
            set.DeserializeFromJsonString(customSetJson);
            Setup.Load(set, (HumanWeapon)humanWeapon, false);
            Stats = HumanStats.Deserialize(Stats, Setup.CustomSet.Stats.Value);
            if (!SettingsManager.InGameCurrent.Misc.CustomPerks.Value)
                Stats.DisablePerks();
            if (!SettingsManager.InGameCurrent.Misc.CustomStats.Value)
            {
                Stats.Acceleration = 100;
                Stats.Speed = 75;
                Stats.Gas = 75;
                Stats.Ammunition = 70;
                Stats.ResetGas();
                Stats.UpdateStats();
            }
            bool isGun = humanWeapon == (int)HumanWeapon.AHSS || humanWeapon == (int)HumanWeapon.APG;

            if (HookLeft != null)
                HookLeft.LocalClearAllHooks();
            if (HookRight != null)
                HookRight.LocalClearAllHooks();

            HookLeft = new HookUseable(this, true, isGun);
            HookRight = new HookUseable(this, false, isGun);
            bool male = Setup.CustomSet.Sex.Value == (int)HumanSex.Male;
            RunAnimation = HumanAnimations.Run;
            if (Setup.Weapon == HumanWeapon.AHSS || Setup.Weapon == HumanWeapon.APG)
                StandAnimation = male ? HumanAnimations.IdleAHSSM : HumanAnimations.IdleAHSSF;
            else if (Setup.Weapon == HumanWeapon.Thunderspear)
            {
                StandAnimation = male ? HumanAnimations.IdleTSM : HumanAnimations.IdleTSF;
                RunAnimation = HumanAnimations.RunTS;
            }
            else
                StandAnimation = male ? HumanAnimations.IdleM : HumanAnimations.IdleF;
            if (IsMine())
            {
                SetupWeapon(humanWeapon);
                SetupItems();
                SetSpecial(SettingsManager.InGameCharacterSettings.Special.Value);
            }
            FinishSetup = true;
            // ignore if name contains char_eyes, char_face, char_glasses
            List<string> namesToIgnore = new List<string> { "char_eyes", "char_face", "char_glasses" };

            if (this.OutlineComponent != null)
                this.OutlineComponent.RefreshRenderers(namesToIgnore);
            CustomAnimationSpeed();
            StartCoroutine(WaitAndNotifyReloaded());
        }

        protected void SetupWeapon(int humanWeapon)
        {
            if (humanWeapon == (int)HumanWeapon.Blade)
            {
                var bladeInfo = CharacterData.HumanWeaponInfo["Blade"];
                float durability = Stats.Ammunition * 3f - 140f;
                int bladeCount = bladeInfo["Blades"].AsInt;
                if (Stats.Perks["DurableBlades"].CurrPoints > 0)
                {
                    durability *= 2f;
                    bladeCount = Mathf.FloorToInt(bladeCount * 0.5f);
                }
                Weapon = new BladeWeapon(this, durability, bladeCount);
            }
            else if (humanWeapon == (int)HumanWeapon.AHSS)
            {
                var gunInfo = CharacterData.HumanWeaponInfo["AHSS"];
                Weapon = new AHSSWeapon(this, Mathf.Clamp(Mathf.FloorToInt(Stats.Ammunition * 0.5f) - 22, 4, 30), gunInfo["AmmoRound"].AsInt, gunInfo["CD"].AsFloat);
            }
            else if (humanWeapon == (int)HumanWeapon.APG)
            {
                JSONNode gunInfo;
                if (SettingsManager.InGameCurrent.Misc.APGPVP.Value)
                    gunInfo = CharacterData.HumanWeaponInfo["APGPVP"];
                else
                    gunInfo = CharacterData.HumanWeaponInfo["APG"];
                Weapon = new APGWeapon(this, Mathf.Clamp(Mathf.FloorToInt(Stats.Ammunition * 0.7f) - 30, 2, 50), gunInfo["AmmoRound"].AsInt, gunInfo["CD"].AsFloat);
            }
            else if (humanWeapon == (int)HumanWeapon.Thunderspear)
            {
                var tsInfo = CharacterData.HumanWeaponInfo["Thunderspear"];
                if (SettingsManager.InGameCurrent.Misc.ThunderspearPVP.Value)
                {

                    
                    int radiusStat = SettingsManager.AbilitySettings.BombRadius.Value;
                    int cdStat = SettingsManager.AbilitySettings.BombCooldown.Value;
                    int speedStat = SettingsManager.AbilitySettings.BombSpeed.Value;
                    int rangeStat = SettingsManager.AbilitySettings.BombRange.Value;
                    float range = BombUtil.GetBombRange(rangeStat, 0f, 4f, 7f);
                    float speed = BombUtil.GetBombSpeed(speedStat, 3f, 10.5f, 10.5f);
                    float travelTime = range / speed;
                    float radius = BombUtil.GetBombRadius(radiusStat, 5.40f, 7.4f, 7f);
                    float cd = BombUtil.GetBombCooldown(cdStat, 4f, 7f, 7f);
                    Weapon = new ThunderspearWeapon(this, -1, -1, cd, radius, speed, travelTime, 0f, tsInfo);
                    if (CustomLogicManager.Evaluator.CurrentTime > 10f)
                        Weapon.SetCooldownLeft(5f);
                    else
                        Weapon.SetCooldownLeft(10f);
                }
                else
                {
                    float travelTime = tsInfo["Range"].AsFloat / tsInfo["Speed"].AsFloat;
                    Weapon = new ThunderspearWeapon(this, Mathf.Clamp(Mathf.FloorToInt(Stats.Ammunition * 0.5f) - 20, 4, 30), tsInfo["AmmoRound"].AsInt, tsInfo["CD"].AsFloat, tsInfo["Radius"].AsFloat,
                        tsInfo["Speed"].AsFloat, travelTime, tsInfo["Delay"].AsFloat, tsInfo);
                }
            }
        }

        protected void SetupItems()
        {
            float cooldown = 30f;
            Items.Clear();
            Items.Add(new FlareItem(this, "Green", new Color(0f, 1f, 0f, 0.7f), cooldown));
            Items.Add(new FlareItem(this, "Red", new Color(1f, 0f, 0f, 0.7f), cooldown));
            Items.Add(new FlareItem(this, "Black", new Color(0f, 0f, 0f, 0.7f), cooldown));
            Items.Add(new FlareItem(this, "Purple", new Color(153f / 255, 0f, 204f / 255, 0.7f), cooldown));
            Items.Add(new FlareItem(this, "Blue", new Color(0f, 102f / 255, 204f / 255, 0.7f), cooldown));
            Items.Add(new FlareItem(this, "Yellow", new Color(1f, 1f, 0f, 0.7f), cooldown));
        }

        public void SetSpecial(string special)
        {
            CurrentSpecial = special;
            Special = HumanSpecials.GetSpecialUseable(this, special);
            ((InGameMenu)UIManager.CurrentMenu).HUDBottomHandler.SetSpecialIcon(HumanSpecials.GetSpecialIcon(special));
        }

        protected void LoadSkin(Player player = null)
        {
            if (IsMine())
            {
                if (SettingsManager.CustomSkinSettings.Human.SkinsEnabled.Value)
                {
                    HumanCustomSkinSet set = (HumanCustomSkinSet)SettingsManager.CustomSkinSettings.Human.GetSelectedSet();
                    string url = string.Join(",", new string[] { set.Horse.Value, set.Hair.Value, set.Eye.Value, set.Glass.Value, set.Face.Value,
                set.Skin.Value, set.Costume.Value, set.Logo.Value, set.GearL.Value, set.GearR.Value, set.Gas.Value, set.Hoodie.Value,
                    set.WeaponTrail.Value, set.ThunderspearL.Value, set.ThunderspearR.Value, set.HookLTiling.Value.ToString(), set.HookL.Value,
                    set.HookRTiling.Value.ToString(), set.HookR.Value, set.Hat.Value, set.Head.Value, set.Back.Value });
                    int viewID = -1;
                    if (Horse != null)
                    {
                        viewID = Horse.gameObject.GetPhotonView().ViewID;
                    }
                    if (player == null)
                        Cache.PhotonView.RPC("LoadSkinRPC", RpcTarget.All, new object[] { viewID, url });
                    else
                        Cache.PhotonView.RPC("LoadSkinRPC", player, new object[] { viewID, url });
                }
                else
                    _customSkinLoader.Finished = true;
            }
        }

        [PunRPC]
        public void LoadSkinRPC(int horse, string url, PhotonMessageInfo info)
        {
            if (info.Sender != photonView.Owner || !FinishSetup)
                return;
            HumanCustomSkinSettings settings = SettingsManager.CustomSkinSettings.Human;
            if (settings.SkinsEnabled.Value && (!settings.SkinsLocal.Value || photonView.IsMine))
            {
                StartCoroutine(_customSkinLoader.LoadSkinsFromRPC(new object[] { horse, url }));
            }
            else
                _customSkinLoader.Finished = true;
        }

        [PunRPC]
        public void SetHookStateRPC(bool left, int hookId, int state, PhotonMessageInfo info)
        {
            if (!FinishSetup)
                return;
            if (left)
                HookLeft.Hooks[hookId].OnSetHookState(state, info);
            else
                HookRight.Hooks[hookId].OnSetHookState(state, info);
        }

        [PunRPC]
        public void SetHookingRPC(bool left, int hookId, Vector3 baseVelocity, Vector3 relativeVelocity, PhotonMessageInfo info)
        {
            if (!FinishSetup)
                return;
            if (left)
                HookLeft.Hooks[hookId].OnSetHooking(baseVelocity, relativeVelocity, info);
            else
                HookRight.Hooks[hookId].OnSetHooking(baseVelocity, relativeVelocity, info);
        }

        [PunRPC]
        public void SetHookedRPC(bool left, int hookId, Vector3 position, int viewId, int objectId, PhotonMessageInfo info)
        {
            if (!FinishSetup)
                return;
            if (left)
                HookLeft.Hooks[hookId].OnSetHooked(position, viewId, objectId, info);
            else
                HookRight.Hooks[hookId].OnSetHooked(position, viewId, objectId, info);
        }

        [PunRPC]
        public void SetSmokeRPC(bool active, PhotonMessageInfo info)
        {
            if (info.Sender != Cache.PhotonView.Owner || !FinishSetup)
                return;
            var emission = HumanCache.Smoke.emission;
            emission.enabled = active;
        }

        protected void ToggleSparks(bool toggle)
        {
            if (!IsMine())
                return;
            ToggleSound(HumanSounds.Slide, toggle);
            if (toggle != HumanCache.Sparks.emission.enabled)
                Cache.PhotonView.RPC("ToggleSparksRPC", RpcTarget.All, new object[] { toggle });
        }

        [PunRPC]
        protected void ToggleSparksRPC(bool toggle, PhotonMessageInfo info)
        {
            if (info.Sender != Cache.PhotonView.Owner || !FinishSetup)
                return;
            var emission = HumanCache.Sparks.emission;
            emission.enabled = toggle;
        }

        public void SetThunderspears(bool hasLeft, bool hasRight)
        {
            if (!IsMine())
                return;
            photonView.RPC("SetThunderspearsRPC", RpcTarget.All, new object[] { hasLeft, hasRight });
        }

        [PunRPC]
        public void SetThunderspearsRPC(bool hasLeft, bool hasRight, PhotonMessageInfo info)
        {
            if (info.Sender != photonView.Owner || !FinishSetup)
                return;
            if (Setup._part_blade_l != null)
                Setup._part_blade_l.SetActive(hasLeft);
            if (Setup._part_blade_r != null)
                Setup._part_blade_r.SetActive(hasRight);
        }

        public void OnHooked(bool left, Vector3 position)
        {
            // If reel in holding is disabled, when the user launches a new hook, reset the wait for release flag.
            if (!SettingsManager.InputSettings.Human.ReelInHolding.Value)
                _reelInWaitForRelease = false;
            if (left)
            {
                _launchLeft = true;
                _launchLeftTime = 0f;
            }
            else
            {
                _launchRight = true;
                _launchRightTime = 0f;
            }
            // There is no need to judge the mountState of human, whether hookable is desiced by controller
            if (State == HumanState.Grab || State == HumanState.Reload || State == HumanState.Stun)
                return;
            if (MountState != HumanMountState.None)
                Unmount(true);
            if (CarryState == HumanCarryState.Carry)
                Cache.PhotonView.RPC("UncarryRPC", RpcTarget.All, new object[0]);
            if (State != HumanState.Attack && State != HumanState.SpecialAttack)
                Idle();
            Vector3 v = (position - Cache.Transform.position).normalized * 20f;
            if (IsHookedLeft() && IsHookedRight())
                v *= 0.8f;
            if (State != HumanState.SpecialAttack)
            {
                FalseAttack();
                Idle();
                if (Setup.Weapon == HumanWeapon.AHSS || Setup.Weapon == HumanWeapon.APG)
                    CrossFade(HumanAnimations.AHSSHookForwardBoth, 0.1f);
                else if (left && !IsHookedRight())
                    CrossFade(HumanAnimations.AirHookLJust, 0.1f);
                else if (!left && !IsHookedLeft())
                    CrossFade(HumanAnimations.AirHookRJust, 0.1f);
                else
                {
                    CrossFade(HumanAnimations.Dash, 0.1f);
                }
            }
            Vector3 force = v;
            if (v.y < 30f)
                force += Vector3.up * (30f - v.y);
            if (position.y >= Cache.Transform.position.y)
                force += Vector3.up * (position.y - Cache.Transform.position.y) * 10f;
            Cache.Rigidbody.AddForce(force);
            TargetAngle = Mathf.Atan2(force.x, force.z) * Mathf.Rad2Deg;
            _targetRotation = GetTargetRotation();
            Cache.Transform.rotation = _targetRotation;
            Cache.Rigidbody.rotation = _targetRotation;
            ToggleSparks(false);
            _cancelGasDisable = true;
        }

        public void OnHookedHuman(bool left, Vector3 position, Human human)
        {
            if (State == HumanState.Grab || MountState == HumanMountState.MapObject || State == HumanState.Stun)
                return;
            if (!human.Dead && human != this)
            {
                _hookHuman = human;
                _hookHumanLeft = left;
                human.Cache.PhotonView.RPC("OnHookedByHuman", human.Cache.PhotonView.Owner, new object[] { Cache.PhotonView.ViewID });
                Vector3 launchForce = position - Cache.Transform.position;
                float num = Mathf.Pow(launchForce.magnitude, 0.1f);
                if (Grounded)
                    Cache.Rigidbody.AddForce(Vector3.up * Mathf.Min(launchForce.magnitude * 0.2f, (10f)), ForceMode.Impulse);
                Cache.Rigidbody.AddForce(launchForce * num * 0.1f, ForceMode.Impulse);
                _hookHumanConstantTimeLeft = 1f;
            }
        }

        public void UnhookHuman(bool left)
        {
            if (left == _hookHumanLeft)
                _hookHuman = null;
        }

        [PunRPC]
        public void OnHookedByHuman(int viewId, PhotonMessageInfo info)
        {
            var human = Util.FindCharacterByViewId(viewId);
            if (IsMine() && human != null && !Dead && human.Cache.PhotonView.Owner == info.Sender &&
                State != HumanState.Grab && CarryState != HumanCarryState.Carry && MountState == HumanMountState.None && human != this)
            {
                Vector3 direction = human.Cache.Transform.position - Cache.Transform.position;
                float loss = CharacterData.HumanWeaponInfo["Hook"]["InitialVelocityLoss"].AsFloat;
                Cache.Rigidbody.AddForce(-Cache.Rigidbody.velocity * loss, ForceMode.VelocityChange);
                float num = Mathf.Pow(direction.magnitude, 0.1f);
                if (Grounded)
                    Cache.Rigidbody.AddForce(Vector3.up * Mathf.Min(direction.magnitude * 0.2f, 10f), ForceMode.Impulse);
                Cache.Rigidbody.AddForce(direction * num * CharacterData.HumanWeaponInfo["Hook"]["InitialPullForce"].AsFloat, ForceMode.Impulse);
                CrossFade(HumanAnimations.Dash, 0.05f, 0.1f / Animation.GetLength(HumanAnimations.Dash));
                State = HumanState.Stun;
                _stateTimeLeft = CharacterData.HumanWeaponInfo["Hook"]["StunTime"].AsFloat;
                FalseAttack();
                float facingDirection = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                Quaternion quaternion = Quaternion.Euler(0f, facingDirection, 0f);
                Cache.Rigidbody.rotation = quaternion;
                Cache.Transform.rotation = quaternion;
                _targetRotation = quaternion;
                TargetAngle = facingDirection;
            }
        }

        [PunRPC]
        public void OnStillHookedByHuman(int viewId, PhotonMessageInfo info)
        {
            var human = Util.FindCharacterByViewId(viewId);
            if (IsMine() && human != null && !Dead && human.Cache.PhotonView.Owner == info.Sender &&
                State != HumanState.Grab && CarryState != HumanCarryState.Carry && MountState == HumanMountState.None && human != this)
            {
                float loss = CharacterData.HumanWeaponInfo["Hook"]["ConstantVelocityLoss"].AsFloat;
                Cache.Rigidbody.AddForce(-Cache.Rigidbody.velocity * loss, ForceMode.VelocityChange);
                float constantPullForce = CharacterData.HumanWeaponInfo["Hook"]["ConstantPullForce"].AsFloat;
                if (constantPullForce > 0f)
                {
                    Vector3 direction = human.Cache.Transform.position - Cache.Transform.position;
                    float num = Mathf.Pow(direction.magnitude, 0.1f);
                    Cache.Rigidbody.AddForce(direction * num * constantPullForce, ForceMode.Impulse);
                }
            }
        }

        public void GetStunnedByTS(Vector3 origin)
        {
            Vector3 direction = Cache.Transform.position - origin;
            Cache.Rigidbody.AddForce(direction.normalized * CharacterData.HumanWeaponInfo["Thunderspear"]["StunForce"].AsFloat, ForceMode.VelocityChange);
            CrossFade(HumanAnimations.Dash, 0.05f, 0.1f / Animation.GetLength(HumanAnimations.Dash));
            State = HumanState.Stun;
            _stateTimeLeft = CharacterData.HumanWeaponInfo["Thunderspear"]["StunDuration"].AsFloat;
            FalseAttack();
            float facingDirection = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            Quaternion quaternion = Quaternion.Euler(0f, facingDirection, 0f);
            Cache.Rigidbody.rotation = quaternion;
            Cache.Transform.rotation = quaternion;
            _targetRotation = quaternion;
            TargetAngle = facingDirection;
            Stats.UseTSGas();
            ((InGameMenu)UIManager.CurrentMenu).HUDBottomHandler.ShakeGas();
            EffectSpawner.Spawn(EffectPrefabs.GasBurst, Cache.Transform.position, Cache.Transform.rotation);
            PlaySound(HumanSounds.GasBurst);
            ((InGameCamera)SceneLoader.CurrentCamera).StartShake();
        }

        public void SetInterpolation(bool interpolate)
        {
            if (IsMine() && interpolate && SettingsManager.GraphicsSettings.InterpolationEnabled.Value)
                Cache.Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            else
                Cache.Rigidbody.interpolation = RigidbodyInterpolation.None;
        }

        private void SetTriggerCollider(bool trigger)
        {
            if (IsMine())
            {
                _isTrigger = trigger;
                Cache.PhotonView.RPC("SetTriggerColliderRPC", RpcTarget.All, new object[] { trigger });
            }
        }

        [PunRPC]
        public void SetTriggerColliderRPC(bool trigger, PhotonMessageInfo info)
        {
            if (info.Sender != Cache.PhotonView.Owner)
                return;
            GetComponent<CapsuleCollider>().isTrigger = trigger;
        }

        private float GetReelAxis()
        {
            if (ReelInAxis != 0f)
                return ReelInAxis;
            return ReelOutAxis;
        }

        private float GetLeanAngle(Vector3 hookPosition, bool left)
        {
            if (Setup.Weapon != HumanWeapon.AHSS && Setup.Weapon != HumanWeapon.APG && State == HumanState.Attack)
                return 0f;
            float height = hookPosition.y - Cache.Transform.position.y;
            float dist = Vector3.Distance(hookPosition, Cache.Transform.position);
            float angle = Mathf.Acos(height / dist) * Mathf.Rad2Deg * 0.1f * (1f + Mathf.Pow(Cache.Rigidbody.velocity.magnitude, 0.2f));
            Vector3 v = hookPosition - Cache.Transform.position;
            float current = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
            float target = Mathf.Atan2(Cache.Rigidbody.velocity.x, Cache.Rigidbody.velocity.z) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(current, target);
            angle += Mathf.Abs(delta * 0.5f);
            if (State != HumanState.Attack)
                angle = Mathf.Min(angle, 80f);
            _leanLeft = delta > 0f;
            if (Setup.Weapon == HumanWeapon.AHSS || Setup.Weapon == HumanWeapon.APG)
                return angle * (delta >= 0f ? 1f : -1f);
            float multiplier = 0.5f;
            if ((left && delta < 0f) || (!left && delta > 0f))
                multiplier = 0.1f;
            return angle * (delta >= 0f ? multiplier : -multiplier);
        }

        public bool CanBladeAttack()
        {
            return Weapon is BladeWeapon && ((BladeWeapon)Weapon).CurrentDurability > 0f && State == HumanState.Idle;
        }

        public void StartSpecialAttack(string animation)
        {
            if (State == HumanState.Attack || State == HumanState.SpecialAttack)
                FalseAttack();
            PlayAnimation(animation);
            State = HumanState.SpecialAttack;
            ToggleSparks(false);
        }

        public void ActivateBlades()
        {
            if (!HumanCache.BladeHitLeft.IsActive())
            {
                HumanCache.BladeHitLeft.Activate();
                ToggleBladeTrails(true);
            }
            if (!HumanCache.BladeHitRight.IsActive())
            {
                HumanCache.BladeHitRight.Activate();
                ToggleBladeTrails(true);
            }
        }

        public void StartBladeSwing()
        {
            if (!Grounded && (HookLeft.IsHooked() || HookRight.IsHooked() || MountState != HumanMountState.None))
            {
                if (SettingsManager.InputSettings.General.Left.GetKey())
                    AttackAnimation = (UnityEngine.Random.Range(0, 100) >= 50) ? HumanAnimations.Attack1HookL1 : HumanAnimations.Attack1HookL2;
                else if (SettingsManager.InputSettings.General.Right.GetKey())
                    AttackAnimation = (UnityEngine.Random.Range(0, 100) >= 50) ? HumanAnimations.Attack1HookR1 : HumanAnimations.Attack1HookR2;
                else if (_leanLeft)
                    AttackAnimation = (UnityEngine.Random.Range(0, 100) >= 50) ? HumanAnimations.Attack1HookL1 : HumanAnimations.Attack1HookL2;
                else
                    AttackAnimation = (UnityEngine.Random.Range(0, 100) >= 50) ? HumanAnimations.Attack1HookR1 : HumanAnimations.Attack1HookR2;
            }
            else if (SettingsManager.InputSettings.General.Left.GetKey())
                AttackAnimation = HumanAnimations.Attack2;
            else if (SettingsManager.InputSettings.General.Right.GetKey())
                AttackAnimation = HumanAnimations.Attack1;
            else
            {
                BaseTitan titan = FindNearestTitan();
                if (titan != null)
                    AttackAnimation = GetBladeAnimationTarget(titan.BaseTitanCache.Neck);
                else
                    AttackAnimation = GetBladeAnimationMouse();
            }
            if (Grounded)
            {
                Cache.Rigidbody.AddForce(Cache.Transform.forward * 200f);
            }
            PlayAnimationReset(AttackAnimation);
            _attackButtonRelease = false;
            State = HumanState.Attack;
            if (Grounded)
            {
                _attackRelease = true;
                _attackButtonRelease = true;
            }
            else
                _attackRelease = false;
            ToggleSparks(false);
        }

        private string GetBladeAnimationMouse()
        {
            if (CursorManager.GetInGameMousePosition().x < (Screen.width * 0.5))
                return HumanAnimations.Attack2;
            else
                return HumanAnimations.Attack1;
        }

        private string GetBladeAnimationTarget(Transform target)
        {
            Vector3 v = target.position - Cache.Transform.position;
            float current = -Mathf.Atan2(v.z, v.x) * Mathf.Rad2Deg;
            float delta = -Mathf.DeltaAngle(current, Cache.Transform.rotation.eulerAngles.y - 90f);
            if (((Mathf.Abs(delta) < 90f) && (v.magnitude < 6f)) && ((target.position.y <= (Cache.Transform.position.y + 2f)) && (target.position.y >= (Cache.Transform.position.y - 5f))))
                return HumanAnimations.Attack4;
            else if (delta > 0f)
                return HumanAnimations.Attack1;
            else
                return HumanAnimations.Attack2;
        }

        private BaseTitan FindNearestTitan()
        {
            float nearestDistance = float.PositiveInfinity;
            BaseTitan nearestTitan = null;
            foreach (BaseTitan titan in _inGameManager.Titans)
            {
                float distance = Vector3.Distance(Cache.Transform.position, titan.Cache.Transform.position);
                if (distance < nearestDistance)
                {
                    nearestTitan = titan;
                    nearestDistance = distance;
                }
            }
            foreach (BaseTitan titan in _inGameManager.Shifters)
            {
                float distance = Vector3.Distance(Cache.Transform.position, titan.Cache.Transform.position);
                if (distance < nearestDistance)
                {
                    nearestTitan = titan;
                    nearestDistance = distance;
                }
            }
            return nearestTitan;
        }

        private Human FindNearestHuman()
        {
            float nearestDistance = float.PositiveInfinity;
            Human nearestHuman = null;
            foreach (Human human in _inGameManager.Humans)
            {
                if (human != this && TeamInfo.SameTeam(human, Team))
                {
                    float distance = Vector3.Distance(Cache.Transform.position, human.Cache.Transform.position);
                    if (distance < nearestDistance)
                    {
                        nearestHuman = human;
                        nearestDistance = distance;
                    }
                }
            }
            return nearestHuman;
        }

        private void FalseAttack()
        {
            if (Setup.Weapon == HumanWeapon.AHSS || Setup.Weapon == HumanWeapon.Thunderspear || Setup.Weapon == HumanWeapon.APG)
            {
                if (!_attackRelease)
                    _attackRelease = true;
            }
            else
            {
                ToggleBladeTrails(false);
                HumanCache.BladeHitLeft.Deactivate();
                HumanCache.BladeHitRight.Deactivate();
                if (!_attackRelease)
                {
                    ContinueAnimation();
                    _attackRelease = true;
                }
            }
        }

        public void ContinueAnimation()
        {
            if (!_animationStopped)
                return;
            _animationStopped = false;
            Cache.PhotonView.RPC("ContinueAnimationRPC", RpcTarget.All, new object[0]);
        }

        [PunRPC]
        public void ContinueAnimationRPC(PhotonMessageInfo info)
        {
            if (info.Sender != Cache.PhotonView.Owner)
                return;
            Animation.SetSpeedAll(1f);
            CustomAnimationSpeed();
            string animationName = GetCurrentAnimation();
            if (animationName != "")
                PlayAnimation(animationName);
        }

        public void PauseAnimation()
        {
            if (_animationStopped)
                return;
            _animationStopped = true;
            Cache.PhotonView.RPC("PauseAnimationRPC", RpcTarget.All, new object[0]);
        }

        [PunRPC]
        public void PauseAnimationRPC(PhotonMessageInfo info)
        {
            if (info.Sender != Cache.PhotonView.Owner)
                return;
            Animation.SetSpeedAll(0f);
        }

        private void CustomAnimationSpeed()
        {
            Animation.SetSpeed(HumanAnimations.SpecialLevi, 1.85f);
            Animation.SetSpeed(HumanAnimations.ChangeBlade, 1.2f);
            Animation.SetSpeed(HumanAnimations.AirRelease, 0.6f);
            Animation.SetSpeed(HumanAnimations.ChangeBladeAir, 0.8f);
            Animation.SetSpeed(HumanAnimations.AHSSGunReloadBoth, 0.38f);
            Animation.SetSpeed(HumanAnimations.AHSSGunReloadBothAir, 0.5f);
            Animation.SetSpeed(HumanAnimations.SpecialShifter, 0.3f);
            if (Setup.Weapon == HumanWeapon.Thunderspear)
            {
                Animation.SetSpeed(HumanAnimations.AHSSGunReloadBoth, 0.76f);
                Animation.SetSpeed(HumanAnimations.AHSSGunReloadBothAir, 1f);
            }
            int refillPoints = Stats.Perks["RefillTime"].CurrPoints;
            Animation.SetSpeed(HumanAnimations.Refill, refillPoints + 1);
        }

        private bool HasHook()
        {
            return HookLeft.HasHook() || HookRight.HasHook();
        }

        private bool IsHookedAny()
        {
            return IsHookedLeft() || IsHookedRight();
        }

        private bool IsHookedLeft()
        {
            return HookLeft.IsHooked();
        }

        private bool IsHookedRight()
        {
            return HookRight.IsHooked();
        }

        private bool IsFrontGrounded()
        {
            return CheckRaycastIgnoreTriggers(Cache.Transform.position + Cache.Transform.up * 1f, Cache.Transform.forward, 1f, GroundMask.value);
        }

        private bool IsPressDirectionTowardsHero()
        {
            if (!HasDirection)
                return false;
            return (Mathf.Abs(Mathf.DeltaAngle(TargetAngle, Cache.Transform.rotation.eulerAngles.y)) < 45f);
        }

        private bool IsPressDirectionRelativeToWall(Vector3 wallNormal, float dotValue)
        {
            if (!HasDirection)
                return false;
            float dotProduct = Vector3.Dot(GetTargetDirection(), wallNormal);
            return dotProduct > dotValue;
        }

        private bool IsUpFrontGrounded()
        {
            return CheckRaycastIgnoreTriggers(Cache.Transform.position + Cache.Transform.up * 3f, Cache.Transform.forward, 1.2f, GroundMask.value);
        }

        public bool IsFiringThunderspear()
        {
            return Setup.Weapon == HumanWeapon.Thunderspear && (Animation.IsPlaying(HumanAnimations.TSShootL) || Animation.IsPlaying(HumanAnimations.TSShootR) || Animation.IsPlaying(HumanAnimations.TSShootLAir) || Animation.IsPlaying(HumanAnimations.TSShootRAir));
        }

        private void ToggleBladeFire(int state)
        {
            if (IsMine())
            {
                if (state != _bladeFireState)
                    Cache.PhotonView.RPC("ToggleBladeFireRPC", RpcTarget.All, new object[] { state });
                _bladeFireState = state;
            }
        }

        private void ToggleBladeTrails(bool toggle)
        {
            if (IsMine())
            {
                if (toggle != _bladeTrailActive)
                    Cache.PhotonView.RPC("ToggleBladeTrailsRPC", RpcTarget.All, new object[] { toggle });
                _bladeTrailActive = toggle;
            }
        }

        public void ToggleBlades(bool toggle)
        {
            if (IsMine())
                Cache.PhotonView.RPC("ToggleBladesRPC", RpcTarget.All, new object[] { toggle });
        }

        [PunRPC]
        protected void ToggleBladesRPC(bool toggle, PhotonMessageInfo info)
        {
            if (info.Sender != null && info.Sender != Cache.PhotonView.Owner)
                return;
            if (!FinishSetup)
                return;
            if (toggle)
            {
                Setup._part_blade_l.SetActive(true);
                Setup._part_blade_r.SetActive(true);
            }
            else
            {
                if (Setup.LeftTrail != null && Setup.RightTrail != null)
                {
                    Setup.LeftTrail.StopImmediate();
                    Setup.RightTrail.StopImmediate();
                }
                Setup._part_blade_l.SetActive(false);
                Setup._part_blade_r.SetActive(false);
            }
        }

        [PunRPC]
        protected void ToggleBladeTrailsRPC(bool toggle, PhotonMessageInfo info)
        {
            if (info.Sender != null && info.Sender != Cache.PhotonView.Owner)
                return;
            if (Setup == null || Setup.LeftTrail == null || Setup.RightTrail == null)
                return;
            bool canShowTrail = SettingsManager.GraphicsSettings.WeaponTrail.Value == (int)WeaponTrailMode.All
                                || (SettingsManager.GraphicsSettings.WeaponTrail.Value == (int)WeaponTrailMode.Mine && IsMine());
            if (toggle && canShowTrail)
            {
                Setup.LeftTrail.Emit = true;
                Setup.RightTrail.Emit = true;
                Setup.LeftTrail._emitTime = 0f;
                Setup.RightTrail._emitTime = 0f;
            }
            else
            {
                Setup.LeftTrail._emitTime = 0.1f;
                Setup.RightTrail._emitTime = 0.1f;
            }

            // if canShowTrail is false and the trails are active, disable them
            if (!canShowTrail && (Setup.LeftTrail.isActiveAndEnabled || Setup.RightTrail.isActiveAndEnabled))
            {
                Setup.LeftTrail.enabled = false;
                Setup.RightTrail.enabled = false;
            }
            else if (canShowTrail && (!Setup.LeftTrail.isActiveAndEnabled || !Setup.RightTrail.isActiveAndEnabled))
            {
                Setup.LeftTrail.enabled = true;
                Setup.RightTrail.enabled = true;
            }
        }

        [PunRPC]
        protected void ToggleBladeFireRPC(int state, PhotonMessageInfo info)
        {
            if (info.Sender != null && info.Sender != Cache.PhotonView.Owner)
                return;
            if (Setup == null || Setup.Weapon != HumanWeapon.Blade || Setup._part_blade_l == null || Setup._part_blade_r == null)
                return;
            var leftFire1 = Setup._part_blade_l.transform.Find("Fire1");
            var leftFire2 = Setup._part_blade_l.transform.Find("Fire2");
            var rightFire1 = Setup._part_blade_r.transform.Find("Fire1");
            var rightFire2 = Setup._part_blade_r.transform.Find("Fire2");
            if (state == 0 || !SettingsManager.GraphicsSettings.WeaponFireEffect.Value)
            {
                leftFire1.gameObject.SetActive(false);
                rightFire1.gameObject.SetActive(false);
                leftFire2.gameObject.SetActive(false);
                rightFire2.gameObject.SetActive(false);
            }
            else if (state == 1)
            {
                leftFire1.gameObject.SetActive(true);
                rightFire1.gameObject.SetActive(true);
                leftFire2.gameObject.SetActive(false);
                rightFire2.gameObject.SetActive(false);
            }
            else if (state == 2)
            {
                leftFire1.gameObject.SetActive(false);
                rightFire1.gameObject.SetActive(false);
                leftFire2.gameObject.SetActive(true);
                rightFire2.gameObject.SetActive(true);
            }
        }

        protected override string GetFootstepAudio(int phase)
        {
            return phase == 0 ? HumanSounds.Footstep1 : HumanSounds.Footstep2;
        }

        protected override int GetFootstepPhase()
        {
            if (Animation.IsPlaying(HumanAnimations.Run) || Animation.IsPlaying(HumanAnimations.RunTS))
            {
                float time = Animation.GetNormalizedTime(HumanAnimations.Run) % 1f;
                return (time >= 0.1f && time < 0.6f) ? 1 : 0;
            }
            else if (Animation.IsPlaying(HumanAnimations.RunBuffed))
            {
                float time = Animation.GetNormalizedTime(HumanAnimations.RunBuffed) % 1f;
                return (time >= 0.1f && time < 0.6f) ? 1 : 0;
            }
            return _stepPhase;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (HumanCache != null)
            {
                if (HumanCache.AHSSHit != null && HumanCache.AHSSHit.gameObject != null)
                    Destroy(HumanCache.AHSSHit.gameObject);
                if (HumanCache.APGHit != null && HumanCache.APGHit.gameObject != null)
                    Destroy(HumanCache.APGHit.gameObject);
            }
            if (Setup != null)
                Setup.DeleteDie();
        }

        protected void EnableSmartTitans()
        {
            int maxSmartTitans = 2;
            int currSmartTitans = 0;
            if (PhotonNetwork.IsMasterClient)
            {
                foreach (var titan in _inGameManager.Titans)
                {
                    if (titan != null && !titan.Dead && titan.AI && titan.IsMine() && titan.Detection.ClosestEnemy == this
                        && Vector3.Distance(Cache.Transform.position, titan.Cache.Transform.position) < titan.GetColliderToggleRadius())
                    {
                        titan.GetComponent<BaseTitanAIController>().SmartAttack = true;
                        currSmartTitans += 1;
                        if (currSmartTitans >= maxSmartTitans)
                            return;
                    }
                }
                foreach (var titan in _inGameManager.Shifters)
                {
                    if (titan != null && !titan.Dead && titan.AI && titan.IsMine() && titan.Detection.ClosestEnemy == this
                        && Vector3.Distance(Cache.Transform.position, titan.Cache.Transform.position) < titan.GetColliderToggleRadius())
                    {
                        titan.GetComponent<BaseTitanAIController>().SmartAttack = true;
                        currSmartTitans += 1;
                        if (currSmartTitans >= maxSmartTitans)
                            return;
                    }
                }
            }
        }

        protected override void CheckGround()
        {

            JustGrounded = false;
            if (CheckRaycastIgnoreTriggers(Cache.Transform.position + Vector3.up * 0.1f, -Vector3.up, GroundDistance, GroundMask.value))
            {
                if (!Grounded)
                    Grounded = JustGrounded = true;
            }
            else if (_needLean && (Setup.Weapon == HumanWeapon.AHSS || Setup.Weapon == HumanWeapon.APG))
            {
                if (CheckRaycastIgnoreTriggers(HumanCache.GroundLeft.position + Vector3.up * 0.1f, -Vector3.up, GroundDistance, GroundMask.value))
                {
                    if (!Grounded)
                        Grounded = JustGrounded = true;
                }
                else if (CheckRaycastIgnoreTriggers(HumanCache.GroundRight.position + Vector3.up * 0.1f, -Vector3.up, GroundDistance, GroundMask.value))
                {
                    if (!Grounded)
                        Grounded = JustGrounded = true;
                }
                else
                    Grounded = false;
            }
            else
                Grounded = false;
        }

        public override bool CheckRaycastIgnoreTriggers(Vector3 origin, Vector3 direction, float distance, int layerMask)
        {
            var hit = RaycastIgnoreTriggers(origin, direction, distance, layerMask);
            if (!hit.HasValue)
                return false;
            var mapObject = MapLoader.GetMapObject(hit.Value.collider.gameObject);
            if (mapObject != null && MapLoader.HasTag(mapObject, "HumanIgnoreGround"))
                return false;
            return true;
        }

        protected override List<Renderer> GetFPSDisabledRenderers()
        {
            List<Renderer> renderers = new List<Renderer>();
            if (FinishSetup && _customSkinLoader.Finished)
            {
                AddRendererIfExists(renderers, Setup._part_head);
                AddRendererIfExists(renderers, Setup._part_hat);
                AddRendererIfExists(renderers, Setup._part_back);
                AddRendererIfExists(renderers, Setup._part_head_decor);
                AddRendererIfExists(renderers, Setup._part_hair, multiple: true);
                AddRendererIfExists(renderers, Setup._part_eye);
                AddRendererIfExists(renderers, Setup._part_glass);
                AddRendererIfExists(renderers, Setup._part_face);
                AddRendererIfExists(renderers, Setup._part_3dmg);
                AddRendererIfExists(renderers, Setup._part_belt);
                AddRendererIfExists(renderers, Setup._part_gas_l);
                AddRendererIfExists(renderers, Setup._part_gas_r);
                AddRendererIfExists(renderers, Setup._part_chest_1);
                AddRendererIfExists(renderers, Setup._part_chest_2);
                AddRendererIfExists(renderers, Setup._part_upper_body);
                AddRendererIfExists(renderers, Setup._part_chest);
                AddRendererIfExists(renderers, Setup._part_brand_1);
                AddRendererIfExists(renderers, Setup._part_brand_2);
                AddRendererIfExists(renderers, Setup._part_brand_3);
                AddRendererIfExists(renderers, Setup._part_brand_4);
                AddRendererIfExists(renderers, Setup._part_arm_l);
                AddRendererIfExists(renderers, Setup._part_arm_r);
                AddRendererIfExists(renderers, Setup._part_leg);
                AddRendererIfExists(renderers, Setup._part_hair_1);
                AddRendererIfExists(renderers, Setup._part_cape);
                AddRendererIfExists(renderers, Setup._part_chest_3);
            }
            return renderers;
        }

        public HumanState State
        {
            get
            {
                return _state;
            }
            set
            {
                if (_state == HumanState.AirDodge || _state == HumanState.GroundDodge)
                    _dashTimeLeft = 0f;
                _state = value;
            }
        }
    }

    public enum HumanState
    {
        Idle,
        Attack,
        GroundDodge,
        AirDodge,
        Reload,
        Refill,
        Die,
        Grab,
        EmoteAction,
        SpecialAttack,
        SpecialAction,
        Slide,
        Run,
        Land,
        MountingHorse,
        Stun,
        WallSlide
    }

    public enum HumanMountState
    {
        None,
        Horse,
        MapObject
    }

    public enum HumanCarryState
    {
        None,
        Carry
    }

    public enum HumanWeapon
    {
        Blade,
        AHSS,
        Thunderspear,
        APG
    }

    public enum HumanDashDirection
    {
        None,
        Forward,
        Back,
        Left,
        Right
    }
}
