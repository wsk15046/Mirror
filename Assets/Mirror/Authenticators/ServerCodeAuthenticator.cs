using System.Collections;
using UnityEngine;

namespace Mirror.Authenticators
{
    /// <summary>
    /// Basic Authenticator that lets the server/host set a "passcode" in order to connect.
    /// <para>
    /// This code could be a short string that can be used to host a private game.
    /// The host would set the code and then give it to their friends allowing them to join.
    /// </para>
    /// </summary>
    [AddComponentMenu("Network/Authenticators/ServerCodeAuthenticator")]
    public class ServerCodeAuthenticator : NetworkAuthenticator
    {
        static readonly ILogger logger = LogFactory.GetLogger(typeof(ServerCodeAuthenticator));

        /// <summary>
        /// Code given to clients so that they can connect to the server/host
        /// <para>
        /// Set this in inspector or at runtime when the server/host starts
        /// </para>
        /// </summary>
        [Header("Custom Properties")]
        public string serverCode;


        /// <summary>
        /// Use whatever credentials make sense for your game.
        /// <para>
        ///     This example uses a code so that only players that know the code can join.
        /// </para>
        /// <para>
        ///     You might want to use an accessToken or passwords. Be aware that the normal connection
        ///     in mirror is not encrypted so sending secure information directly is not adviced
        /// </para>
        /// </summary>
        class AuthRequestMessage : NetworkMessage
        {
            public string serverCode;
        }

        class AuthResponseMessage : NetworkMessage
        {
            public bool success;
            public string message;
        }


        #region Server Authenticate

        /*
            This region should is need to validate the client connection and auth messages sent by the client
         */

        public override void OnStartServer()
        {
            // register a handler for the authentication request we expect from client
            NetworkServer.RegisterHandler<AuthRequestMessage>(OnAuthRequestMessage, false);
        }
        public override void OnServerAuthenticate(NetworkConnection conn)
        {
            // do nothing...wait for AuthRequestMessage from client
        }

        void OnAuthRequestMessage(NetworkConnection conn, AuthRequestMessage msg)
        {
            if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Authentication Request: {0} {1}", msg.serverCode);

            // check if serverCode is empty, or if client sent correct code
            if (string.IsNullOrEmpty(serverCode) || msg.serverCode == serverCode)
            {
                // create and send msg to client so it knows to proceed
                conn.Send(new AuthResponseMessage
                {
                    success = true,
                    message = "Success"
                });

                ServerAccept(conn);
            }
            else
            {
                // create and send msg to client so it knows to disconnect
                AuthResponseMessage authResponseMessage = new AuthResponseMessage
                {
                    success = false,
                    message = "Invalid code"
                };

                conn.Send(authResponseMessage);

                // give time for failed response to be sent
                DelayedDisconnect(conn, 0.2f);
            }
        }

        IEnumerator DelayedDisconnect(NetworkConnection conn, float waitTime)
        {
            yield return new WaitForSeconds(waitTime);
            ServerReject(conn);
        }

        #endregion

        #region Client Authenticate

        /*
            This region should send auth message to the server so that the server can validate it
         */

        public override void OnStartClient()
        {
            // register a handler for the authentication response we expect from server
            NetworkClient.RegisterHandler<AuthResponseMessage>(OnAuthResponseMessage, false);
        }

        public override void OnClientAuthenticate(NetworkConnection conn)
        {
            // OnClientAuthenticate is called when client connects

            // The serverCode should be set on the client before connection to the server.
            // When the client connects it sends the code and the server checks that it is correct
            conn.Send(new AuthRequestMessage
            {
                serverCode = serverCode,
            });
        }

        void OnAuthResponseMessage(NetworkConnection conn, AuthResponseMessage msg)
        {
            if (msg.success)
            {
                if (logger.LogEnabled()) logger.LogFormat(LogType.Log, "Authentication Success: {0}", msg.message);

                // Invoke the event to complete a successful authentication
                OnClientAuthenticated.Invoke(conn);
            }
            else
            {
                logger.LogFormat(LogType.Error, "Authentication Fail: {0}", msg.message);

                // Set this on the client for local reference
                conn.isAuthenticated = false;

                // disconnect the client
                conn.Disconnect();
            }
        }

        #endregion
    }
}
