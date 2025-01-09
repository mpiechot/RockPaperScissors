using Assets;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace RockPaperScissors.Assets
{
    public abstract class SimpleCommunicator : MonoBehaviour, IDisposable
    {
        protected Socket socket;

        private CancellableTaskCollection taskCollection = new();

        private bool isDisposed;

        protected IPEndPoint iPEndPoint = new(IPAddress.Loopback, 5000);

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            socket = new(iPEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            Initialize();

            taskCollection.StartExecution(RunAsync);
        }

        protected abstract void Initialize();

        public virtual void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            taskCollection.Dispose();

            if (socket.IsBound && socket.Connected)
            {
                socket.Shutdown(SocketShutdown.Both);
            }

            socket.Close();
            socket.Dispose();

            isDisposed = true;
        }

        protected abstract UniTask RunAsync(CancellationToken cancellationToken);

        private void OnDestroy()
        {
            Dispose();
        }

        private void OnApplicationQuit()
        {
            Dispose();
        }

        protected async UniTask SendMessageAsync(NetworkMessage messageToSend, Socket handler, CancellationToken cancellationToken)
        {
            var messageJson = JsonConvert.SerializeObject(messageToSend);

            // Sende Nachricht
            var message = $"{messageJson}<|EOM|>";
            var messageBytes = Encoding.UTF8.GetBytes(message);
            _ = await handler.SendAsync(messageBytes, SocketFlags.None);
            Debug.Log($"{gameObject.name} sent message: \"{messageToSend}\"");
        }

        protected bool ProcessResponse(NetworkMessage? response)
        {
            if (response == null)
            {
                // handler lost connection or communication was canceled via cancellation token
                return false;
            }

            switch (response.Code)
            {
                case MessageResponseCode.MES:
                    Debug.Log($"{gameObject.name} received a message: \"{response.Text}\"");
                    return true;
                case MessageResponseCode.SOL:
                    Debug.Log($"{gameObject.name} received the games solution: \"{response.Text}\"");
                    return true;
                case MessageResponseCode.END:
                    Debug.Log($"{gameObject.name} communication ended: \"{response.Text}\"");
                    Dispose();
                    return false;
                case MessageResponseCode.ACK:
                    Debug.Log($"{gameObject.name} received acknowledgment: \"{response.Text}\"");
                    return true;
                case MessageResponseCode.REF:
                    Debug.Log($"{gameObject.name} was refused: \"{response.Text}\"");
                    Dispose();
                    return false;
                case MessageResponseCode.CON:
                    Debug.Log($"{gameObject.name} wants to connect");
                    return true;
                default:
                    Debug.Log($"{gameObject.name} received unknown code: \"{response.Code}\"");
                    Dispose();
                    return false;
            }
        }

        protected async UniTask<NetworkMessage> ReceiveResponseAsync(Socket handler, CancellationToken cancellationToken)
        {
            var buffer = new byte[1_024];

            var received = await handler.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);

            var responseJson = Encoding.UTF8.GetString(buffer, 0, received).Replace("<|EOM|>", string.Empty);
            return JsonConvert.DeserializeObject<NetworkMessage>(responseJson);
        }

    }
}
