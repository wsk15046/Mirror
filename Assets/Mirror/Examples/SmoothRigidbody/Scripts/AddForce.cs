using UnityEngine;

namespace Mirror.Examples.SmoothRigidbody
{
    public class AddForce : NetworkBehaviour
    {
        [SerializeField] float forceJump = 300f;
        [SerializeField] float forceMove = 3f;

        public GameObject ballPrefab;

        private enum MoveType
        {
            Jump,
            Forward,
            Back,
            Left,
            Right
        }

        void Update()
        {
            if (isLocalPlayer)
            {
                MoveController();
            }
        }

        private void MoveController()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                CmdControl(MoveType.Jump);
            }

            if (Input.GetKey(KeyCode.W))
            {
                CmdControl(MoveType.Forward);
            }

            if (Input.GetKey(KeyCode.S))
            {
                CmdControl(MoveType.Back);
            }

            if (Input.GetKey(KeyCode.A))
            {
                CmdControl(MoveType.Left);
            }

            if (Input.GetKey(KeyCode.D))
            {
                CmdControl(MoveType.Right);
            }
        }

        [Command]
        private void CmdControl(MoveType type)
        {
            switch (type)
            {
                case MoveType.Jump:
                    GetComponent<Rigidbody>().AddForce(Vector3.up * forceJump);
                    break;
                case MoveType.Forward:
                    GetComponent<Rigidbody>().AddForce(Vector3.forward * forceMove);
                    break;
                case MoveType.Back:
                    GetComponent<Rigidbody>().AddForce(Vector3.back * forceMove);
                    break;
                case MoveType.Left:
                    GetComponent<Rigidbody>().AddForce(Vector3.left * forceMove);
                    break;
                case MoveType.Right:
                    GetComponent<Rigidbody>().AddForce(Vector3.right * forceMove);
                    break;
            } 
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(5, 150, 300, 800));

            if (isServer)
            {

            }

            if (isClient)
            {
                GUIStyle bb = new GUIStyle();
                bb.fontSize = 20;

                GUILayout.BeginVertical();
                GUILayout.Label(string.Format("  FPS: {0:0.0}", 1/Time.deltaTime), bb);
                if(NetworkClient.isConnected)
                {
                    GUILayout.Label(string.Format("  PING: {0}", NetworkTime.rtt * 100 / 2), bb);
                }
                else
                {
                    GUILayout.Label(string.Format("  PING: #####"), bb);
                }
                
                GUILayout.EndVertical();
            }

            GUILayout.EndArea();
        }

        private void Start()
        {
            Application.targetFrameRate = 60;
        }

    }
}
