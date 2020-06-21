using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace Mirror
{
    /// <summary>
    /// A component to synchronize Mecanim animation states for networked objects.
    /// </summary>
    /// <remarks>
    /// <para>The animation of game objects can be networked by this component. There are two models of authority for networked movement:</para>
    /// <para>If the object has authority on the client, then it should be animated locally on the owning client. The animation state information will be sent from the owning client to the server, then broadcast to all of the other clients. This is common for player objects.</para>
    /// <para>If the object has authority on the server, then it should be animated on the server and state information will be sent to all clients. This is common for objects not related to a specific client, such as an enemy unit.</para>
    /// <para>The NetworkAnimator synchronizes all animation parameters of the selected Animator. It does not automatically sychronize triggers. The function SetTrigger can by used by an object with authority to fire an animation trigger on other clients.</para>
    /// </remarks>
    [AddComponentMenu("Network/NetworkAnimator")]
    [RequireComponent(typeof(NetworkIdentity))]
    [HelpURL("https://mirror-networking.com/docs/Components/NetworkAnimator.html")]
    public class NetworkAnimator : NetworkBehaviour
    {
        static readonly ILogger logger = LogFactory.GetLogger(typeof(NetworkAnimator));

        [Header("Authority")]
        [Tooltip("Set to true if animations come from owner client,  set to false if animations always come from server")]
        public bool clientAuthority;

        /// <summary>
        /// The animator component to synchronize.
        /// </summary>
        [FormerlySerializedAs("animator")]
        [Header("Animator")]
        [Tooltip("Animator that will have parameters synchronized")]
        [SerializeField] Animator _animator = null;

        // Note: not an object[] array because otherwise initialization is real annoying
        int[] lastIntParameters;
        float[] lastFloatParameters;
        bool[] lastBoolParameters;
        AnimatorControllerParameter[] parameters;

        // multiple layers
        int[] animationHash;
        int[] transitionHash;
        float sendTimer;

        bool SendMessagesAllowed
        {
            get
            {
                if (isServer)
                {
                    if (!clientAuthority)
                        return true;

                    // This is a special case where we have client authority but we have not assigned the client who has
                    // authority over it, no animator data will be sent over the network by the server.
                    //
                    // So we check here for a connectionToClient and if it is null we will
                    // let the server send animation data until we receive an owner.
                    if (netIdentity != null && netIdentity.connectionToClient == null)
                        return true;
                }

                return (hasAuthority && clientAuthority);
            }
        }

        public Animator animator
        {
            get => _animator;
            set
            {
                if (HasAnimator)
                {
                    logger.LogError("setting animator when one has already been set");
                    return;
                }

                _animator = value;
                Setup();
            }
        }

        public bool HasAnimator => _animator != null;
        bool hasSetup;
        // cache message if Animator not set
        byte[] setupMsg;

        void Awake()
        {
            // if animator is set in inspector set up automatically 
            if (HasAnimator)
            {
                Setup();
            }
        }

        void Setup()
        {
            logger.Assert(animator.enabled, "Setup called when animator was disabled");

            // store the animator parameters in a variable - the "Animator.parameters" getter allocates
            // a new parameter array every time it is accessed so we should avoid doing it in a loop
            parameters = animator.parameters
                .Where(par => !animator.IsParameterControlledByCurve(par.nameHash))
                .ToArray();
            lastIntParameters = new int[parameters.Length];
            lastFloatParameters = new float[parameters.Length];
            lastBoolParameters = new bool[parameters.Length];

            animationHash = new int[animator.layerCount];
            transitionHash = new int[animator.layerCount];

            if (isServer)
            {
                SendSetupMessage();
            }
            // if server has already sent setup message
            else if (setupMsg != null)
            {
                HandleAnimSetMsgBytes(new ArraySegment<byte>(setupMsg));
                // clear setup message after setup
                setupMsg = null;
            }
        }

        void FixedUpdate()
        {
            if (!hasSetup || !SendMessagesAllowed)
                return;

            if (!animator.enabled)
                return;

            CheckSendRate();

            for (int i = 0; i < animator.layerCount; i++)
            {
                int stateHash;
                float normalizedTime;
                if (!CheckAnimStateChanged(out stateHash, out normalizedTime, i))
                {
                    continue;
                }

                using (PooledNetworkWriter writer = NetworkWriterPool.GetWriter())
                {
                    WriteParameters(writer);
                    SendAnimationMessage(stateHash, normalizedTime, i, writer.ToArray());
                }
            }
        }

        bool CheckAnimStateChanged(out int stateHash, out float normalizedTime, int layerId)
        {
            logger.Assert(HasAnimator, "CheckAnimStateChanged called before animator set");

            stateHash = 0;
            normalizedTime = 0;

            if (animator.IsInTransition(layerId))
            {
                AnimatorTransitionInfo tt = animator.GetAnimatorTransitionInfo(layerId);
                if (tt.fullPathHash != transitionHash[layerId])
                {
                    // first time in this transition
                    transitionHash[layerId] = tt.fullPathHash;
                    animationHash[layerId] = 0;
                    return true;
                }
                return false;
            }

            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(layerId);
            if (st.fullPathHash != animationHash[layerId])
            {
                // first time in this animation state
                if (animationHash[layerId] != 0)
                {
                    // came from another animation directly - from Play()
                    stateHash = st.fullPathHash;
                    normalizedTime = st.normalizedTime;
                }
                transitionHash[layerId] = 0;
                animationHash[layerId] = st.fullPathHash;
                return true;
            }
            return false;
        }

        void CheckSendRate()
        {
            if (SendMessagesAllowed && syncInterval > 0 && sendTimer < Time.time)
            {
                sendTimer = Time.time + syncInterval;

                using (PooledNetworkWriter writer = NetworkWriterPool.GetWriter())
                {
                    if (WriteParameters(writer))
                        SendAnimationParametersMessage(writer.ToArray());
                }
            }
        }

        void SendAnimationMessage(int stateHash, float normalizedTime, int layerId, byte[] parameters)
        {
            if (isServer)
            {
                RpcOnAnimationClientMessage(stateHash, normalizedTime, layerId, parameters);
            }
            else if (ClientScene.readyConnection != null)
            {
                CmdOnAnimationServerMessage(stateHash, normalizedTime, layerId, parameters);
            }
        }

        void SendAnimationParametersMessage(byte[] parameters)
        {
            if (isServer)
            {
                RpcOnAnimationParametersClientMessage(parameters);
            }
            else if (ClientScene.readyConnection != null)
            {
                CmdOnAnimationParametersServerMessage(parameters);
            }
        }

        void HandleAnimMsg(int stateHash, float normalizedTime, int layerId, NetworkReader reader)
        {
            if (hasAuthority && clientAuthority)
                return;
            logger.Assert(HasAnimator, "HandleAnimMsg called before animator set");

            // usually transitions will be triggered by parameters, if not, play anims directly.
            // NOTE: this plays "animations", not transitions, so any transitions will be skipped.
            // NOTE: there is no API to play a transition(?)
            if (stateHash != 0)
            {
                if (animator.enabled)
                    animator.Play(stateHash, layerId, normalizedTime);
            }

            ReadParameters(reader);
        }

        void HandleAnimParamsMsg(NetworkReader reader)
        {
            if (hasAuthority && clientAuthority)
                return;
            logger.Assert(HasAnimator, "HandleAnimParamsMsg called before animator set");

            ReadParameters(reader);
        }

        void HandleAnimTriggerMsg(int hash)
        {
            if (animator.enabled)
            {
                logger.Assert(HasAnimator, "HandleAnimTriggerMsg called before animator set");
                animator.SetTrigger(hash);
            }
        }

        void HandleAnimResetTriggerMsg(int hash)
        {
            if (animator.enabled)
            {
                logger.Assert(HasAnimator, "HandleAnimResetTriggerMsg called before animator set");
                animator.ResetTrigger(hash);
            }
        }

        ulong NextDirtyBits()
        {
            logger.Assert(HasAnimator, "NextDirtyBits called before animator set");

            ulong dirtyBits = 0;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter par = parameters[i];
                bool changed = false;
                if (par.type == AnimatorControllerParameterType.Int)
                {
                    int newIntValue = animator.GetInteger(par.nameHash);
                    changed = newIntValue != lastIntParameters[i];
                    if (changed)
                        lastIntParameters[i] = newIntValue;
                }
                else if (par.type == AnimatorControllerParameterType.Float)
                {
                    float newFloatValue = animator.GetFloat(par.nameHash);
                    changed = Mathf.Abs(newFloatValue - lastFloatParameters[i]) > 0.001f;
                    // only set lastValue if it was changed, otherwise value could slowly drift within the 0.001f limit each frame
                    if (changed)
                        lastFloatParameters[i] = newFloatValue;
                }
                else if (par.type == AnimatorControllerParameterType.Bool)
                {
                    bool newBoolValue = animator.GetBool(par.nameHash);
                    changed = newBoolValue != lastBoolParameters[i];
                    if (changed)
                        lastBoolParameters[i] = newBoolValue;
                }
                if (changed)
                {
                    dirtyBits |= 1ul << i;
                }
            }
            return dirtyBits;
        }

        bool WriteParameters(NetworkWriter writer, bool forceAll = false)
        {
            logger.Assert(HasAnimator, "WriteParameters called before animator set");

            ulong dirtyBits = forceAll ? (~0ul) : NextDirtyBits();
            writer.WritePackedUInt64(dirtyBits);
            for (int i = 0; i < parameters.Length; i++)
            {
                if ((dirtyBits & (1ul << i)) == 0)
                    continue;

                AnimatorControllerParameter par = parameters[i];
                if (par.type == AnimatorControllerParameterType.Int)
                {
                    int newIntValue = animator.GetInteger(par.nameHash);
                    writer.WritePackedInt32(newIntValue);
                }
                else if (par.type == AnimatorControllerParameterType.Float)
                {
                    float newFloatValue = animator.GetFloat(par.nameHash);
                    writer.WriteSingle(newFloatValue);
                }
                else if (par.type == AnimatorControllerParameterType.Bool)
                {
                    bool newBoolValue = animator.GetBool(par.nameHash);
                    writer.WriteBoolean(newBoolValue);
                }
            }
            return dirtyBits != 0;
        }

        void ReadParameters(NetworkReader reader)
        {
            logger.Assert(HasAnimator, "ReadParameters called before animator set");

            bool animatorEnabled = animator.enabled;
            // need to read values from NetworkReader even if animator is disabled

            ulong dirtyBits = reader.ReadPackedUInt64();
            for (int i = 0; i < parameters.Length; i++)
            {
                if ((dirtyBits & (1ul << i)) == 0)
                    continue;

                AnimatorControllerParameter par = parameters[i];
                if (par.type == AnimatorControllerParameterType.Int)
                {
                    int newIntValue = reader.ReadPackedInt32();
                    if (animatorEnabled)
                        animator.SetInteger(par.nameHash, newIntValue);
                }
                else if (par.type == AnimatorControllerParameterType.Float)
                {
                    float newFloatValue = reader.ReadSingle();
                    if (animatorEnabled)
                        animator.SetFloat(par.nameHash, newFloatValue);
                }
                else if (par.type == AnimatorControllerParameterType.Bool)
                {
                    bool newBoolValue = reader.ReadBoolean();
                    if (animatorEnabled)
                        animator.SetBool(par.nameHash, newBoolValue);
                }
            }
        }

        /// <summary>
        /// Causes an animation trigger to be invoked for a networked object.
        /// <para>If local authority is set, and this is called from the client, then the trigger will be invoked on the server and all clients. If not, then this is called on the server, and the trigger will be called on all clients.</para>
        /// </summary>
        /// <param name="triggerName">Name of trigger.</param>
        public void SetTrigger(string triggerName)
        {
            SetTrigger(Animator.StringToHash(triggerName));
        }

        /// <summary>
        /// Causes an animation trigger to be invoked for a networked object.
        /// </summary>
        /// <param name="hash">Hash id of trigger (from the Animator).</param>
        public void SetTrigger(int hash)
        {
            if (clientAuthority)
            {
                if (!isClient)
                {
                    logger.LogWarning("Tried to set animation in the server for a client-controlled animator");
                    return;
                }

                if (!hasAuthority)
                {
                    logger.LogWarning("Only the client with authority can set animations");
                    return;
                }

                if (ClientScene.readyConnection != null)
                    CmdOnAnimationTriggerServerMessage(hash);
            }
            else
            {
                if (!isServer)
                {
                    logger.LogWarning("Tried to set animation in the client for a server-controlled animator");
                    return;
                }

                HandleAnimTriggerMsg(hash);
                RpcOnAnimationTriggerClientMessage(hash);
            }
        }

        /// <summary>
        /// Causes an animation trigger to be reset for a networked object.
        /// <para>If local authority is set, and this is called from the client, then the trigger will be reset on the server and all clients. If not, then this is called on the server, and the trigger will be reset on all clients.</para>
        /// </summary>
        /// <param name="triggerName">Name of trigger.</param>
        public void ResetTrigger(string triggerName)
        {
            ResetTrigger(Animator.StringToHash(triggerName));
        }

        /// <summary>
        /// Causes an animation trigger to be reset for a networked object.
        /// </summary>
        /// <param name="hash">Hash id of trigger (from the Animator).</param>
        public void ResetTrigger(int hash)
        {
            if (clientAuthority)
            {
                if (!isClient)
                {
                    logger.LogWarning("Tried to reset animation in the server for a client-controlled animator");
                    return;
                }

                if (!hasAuthority)
                {
                    logger.LogWarning("Only the client with authority can reset animations");
                    return;
                }

                if (ClientScene.readyConnection != null)
                    CmdOnAnimationResetTriggerServerMessage(hash);
            }
            else
            {
                if (!isServer)
                {
                    logger.LogWarning("Tried to reset animation in the client for a server-controlled animator");
                    return;
                }

                HandleAnimResetTriggerMsg(hash);
                RpcOnAnimationResetTriggerClientMessage(hash);
            }
        }

        #region server message handlers

        [Command]
        void CmdOnAnimationServerMessage(int stateHash, float normalizedTime, int layerId, byte[] parameters)
        {
            // Ignore messages from client if not in client authority mode
            if (!clientAuthority)
                return;

            if (logger.LogEnabled()) logger.Log($"OnAnimationMessage for netId={netId}");

            // handle and broadcast
            using (PooledNetworkReader networkReader = NetworkReaderPool.GetReader(parameters))
            {
                HandleAnimMsg(stateHash, normalizedTime, layerId, networkReader);
                RpcOnAnimationClientMessage(stateHash, normalizedTime, layerId, parameters);
            }
        }

        [Command]
        void CmdOnAnimationParametersServerMessage(byte[] parameters)
        {
            // Ignore messages from client if not in client authority mode
            if (!clientAuthority)
                return;

            // handle and broadcast
            using (PooledNetworkReader networkReader = NetworkReaderPool.GetReader(parameters))
            {
                HandleAnimParamsMsg(networkReader);
                RpcOnAnimationParametersClientMessage(parameters);
            }
        }

        [Command]
        void CmdOnAnimationTriggerServerMessage(int hash)
        {
            // Ignore messages from client if not in client authority mode
            if (!clientAuthority)
                return;

            // handle and broadcast
            HandleAnimTriggerMsg(hash);
            RpcOnAnimationTriggerClientMessage(hash);
        }

        [Command]
        void CmdOnAnimationResetTriggerServerMessage(int hash)
        {
            // Ignore messages from client if not in client authority mode
            if (!clientAuthority)
                return;

            // handle and broadcast
            HandleAnimResetTriggerMsg(hash);
            RpcOnAnimationResetTriggerClientMessage(hash);
        }

        #endregion

        #region client message handlers

        [ClientRpc]
        void RpcOnAnimationClientMessage(int stateHash, float normalizedTime, int layerId, byte[] parameters)
        {
            using (PooledNetworkReader networkReader = NetworkReaderPool.GetReader(parameters))
                HandleAnimMsg(stateHash, normalizedTime, layerId, networkReader);
        }

        [ClientRpc]
        void RpcOnAnimationParametersClientMessage(byte[] parameters)
        {
            using (PooledNetworkReader networkReader = NetworkReaderPool.GetReader(parameters))
                HandleAnimParamsMsg(networkReader);
        }

        [ClientRpc]
        void RpcOnAnimationTriggerClientMessage(int hash)
        {
            // host handles this before it is sent
            if (isServer) return;

            HandleAnimTriggerMsg(hash);
        }

        [ClientRpc]
        void RpcOnAnimationResetTriggerClientMessage(int hash)
        {
            // host handles this before it is sent
            if (isServer) return;

            HandleAnimResetTriggerMsg(hash);
        }

        #endregion

        #region intial setup
        [Server]
        void SendSetupMessage()
        {
            // mark ready on server
            hasSetup = true;

            using (PooledNetworkWriter writer = NetworkWriterPool.GetWriter())
            {
                for (int i = 0; i < animator.layerCount; i++)
                {
                    if (animator.IsInTransition(i))
                    {
                        AnimatorStateInfo st = animator.GetNextAnimatorStateInfo(i);
                        writer.WriteInt32(st.fullPathHash);
                        writer.WriteSingle(st.normalizedTime);
                    }
                    else
                    {
                        AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(i);
                        writer.WriteInt32(st.fullPathHash);
                        writer.WriteSingle(st.normalizedTime);
                    }
                }
                WriteParameters(writer, true);

                ArraySegment<byte> bytes = writer.ToArraySegment();
                RpcOnAnimatorSetClientMessage(bytes);
            }
        }

        [ClientRpc]
        void RpcOnAnimatorSetClientMessage(ArraySegment<byte> setupByte)
        {
            // host players doesnt need to be setup
            if (isServer) { return; }

            logger.Assert(!hasSetup, "Setup should not be called mutliple times");
            // if animator has been set, handle the message right way
            // else wait for animator to be set and handle the message in setup
            if (HasAnimator)
            {
                HandleAnimSetMsgBytes(setupByte);
            }
            else
            {
                setupMsg = setupByte.ToArray();
            }
        }

        void HandleAnimSetMsgBytes(ArraySegment<byte> parameters)
        {
            using (PooledNetworkReader reader = NetworkReaderPool.GetReader(parameters))
            {
                logger.Assert(HasAnimator, "HandleAnimSetMsg called before animator set");
                for (int i = 0; i < animator.layerCount; i++)
                {
                    int stateHash = reader.ReadInt32();
                    float normalizedTime = reader.ReadSingle();
                    animator.Play(stateHash, i, normalizedTime);
                }

                ReadParameters(reader);

                hasSetup = true;
            }
        }
        #endregion
    }
}
