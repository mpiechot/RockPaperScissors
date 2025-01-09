using Assets;
using Cysharp.Threading.Tasks;
using RockPaperScissors.Assets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace RockPaperScissors
{
    public sealed class SimpleServer : SimpleCommunicator
    {
        private ConcurrentDictionary<string, (Socket, string, bool)> connectedClientsMap = new();

        private CancellableTaskCollection connectedClientsTasks = new();

        private RockPaperScissorsServerState state = RockPaperScissorsServerState.WaitingForPlayers;

        private Dictionary<string, bool> sendBackAckToClientMap = new();

        private const int HandleSendingDelay = 100;

        protected override void Initialize()
        {
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                socket.Bind(iPEndPoint);
                socket.Listen(100);

                Debug.Log("[Server] Server started successfully.");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                Debug.Log("[Server] The endpoint is already in use. A server might already be running.");
            }
            catch (Exception ex)
            {
                Debug.Log($"[Server] An error occurred: {ex.Message}");
            }
        }

        protected override async UniTask RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var connectingHandler = await socket.AcceptAsync();

                Debug.Log("[Server] A new client wants to connect " + connectingHandler.LocalEndPoint);

                var clientMessage = await ReceiveResponseAsync(connectingHandler, cancellationToken);

                // Handle clients connection messages
                var connectionValid = clientMessage.Code == MessageResponseCode.CON && connectedClientsMap.Count < 2;
                var messageToSend = new NetworkMessage()
                {
                    PlayerName = gameObject.name,
                    Code = connectionValid ? MessageResponseCode.ACK : MessageResponseCode.REF
                };

                await SendMessageAsync(messageToSend, connectingHandler, cancellationToken);

                if (connectionValid)
                {
                    connectedClientsMap.TryAdd(clientMessage.PlayerName, (connectingHandler, string.Empty, false));
                    connectedClientsTasks.StartExecution((cancellationToken) => HandleClientReceiveMessagesAsync(connectingHandler, clientMessage.PlayerName, cancellationToken));
                    connectedClientsTasks.StartExecution((cancellationToken) => HandleClientSendMessagesAsync(connectingHandler, clientMessage.PlayerName, cancellationToken));
                }
                else
                {
                    connectingHandler.Close();
                    Debug.Log($"[Server] Rejected the connection of {clientMessage.PlayerName}");
                }
            }
        }

        public override void Dispose()
        {
            connectedClientsTasks.Dispose();

            base.Dispose();
        }

        private async UniTask HandleClientReceiveMessagesAsync(Socket client, string clientName, CancellationToken cancellationToken)
        {
            Debug.Log($"[Server] Started handling receive messages for {clientName}.");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Wait for any message of the client
                    var message = await ReceiveResponseAsync(client, cancellationToken);

                    if (message == null)
                    {
                        // handler lost connection or communication was canceled via cancellation token
                        return;
                    }

                    Debug.Log($"[Server] Received message from {clientName}: ({message.Text}, {message.Code})");

                    switch (message.Code)
                    {
                        case MessageResponseCode.MES:
                            // If it is a message, we need to handle it and send a ACK back, so the client knows we received the message
                            HandleMessage(client, message);
                            sendBackAckToClientMap.TryAdd(clientName, true);
                            break;
                        case MessageResponseCode.END:
                            // If it is a 'end connection' message, we need to prepare ending the connection by removing the client of our connected clients list
                            if (connectedClientsMap.TryRemove(clientName, out var _))
                            {
                                Debug.Log($"[Server] Client {clientName} disconnected.");
                            }
                            else
                            {
                                Debug.LogError($"[Server] Tried to remove '{clientName}' but it wasn't in the connected clients map. This should not happen");
                            }

                            return; // End the loop for this client
                        default:
                            Debug.Log($"[Server] Unhandled message code '{message.Code}' received from '{clientName}'");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Server] {clientName} Message Handler threw an Exception: {ex.Message}");
            }
            finally
            {
                client.Close();
                Debug.Log($"[Server] Closed connection for {clientName}.");
            }
        }

        private async UniTask HandleClientSendMessagesAsync(Socket client, string clientName, CancellationToken cancellationToken)
        {
            Debug.Log($"[Server] Started handling sending messages to {clientName}.");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (sendBackAckToClientMap.TryGetValue(clientName, out var sendAck) && sendAck)
                    {
                        // The server needs to send an ACK to the client handled by this async task
                        Debug.Log($"[Server] Sending Ack to {clientName}");
                        await SendMessageAsync(new NetworkMessage() { Code = MessageResponseCode.ACK, PlayerName = "Server" }, client, cancellationToken);
                        sendBackAckToClientMap.Remove(clientName);

                        // Wait a little bit and then try again
                        await UniTask.Delay(HandleSendingDelay);
                        continue;
                    }

                    if (connectedClientsMap.Count < 2)
                    {
                        Debug.Log($"[Server] {clientName} is waiting for other player...");

                        // Wait a little bit and then try again
                        await UniTask.Delay(HandleSendingDelay);
                        continue;
                    }


                    var otherClientName = connectedClientsMap.Keys.FirstOrDefault(key => !clientName.Equals(key, StringComparison.Ordinal));
                    (var _, var otherClientMessage, var solutionSentOther) = connectedClientsMap[otherClientName];
                    (var _, var myMessage, var solutionSentClient) = connectedClientsMap[clientName];

                    if (string.IsNullOrEmpty(otherClientMessage) || string.IsNullOrEmpty(myMessage))
                    {
                        connectedClientsMap.AddOrUpdate(
                            clientName,
                            key => throw new InvalidOperationException($"The client '{key}' was not found in the collection"),
                            (key, oldValue) => (client, myMessage, false)
                        );

                        Debug.Log($"[Server] {clientName} is waiting for both inputs...");

                        // Wait a little bit and then try again
                        await UniTask.Delay(HandleSendingDelay);
                        continue;
                    }

                    if (solutionSentClient && !solutionSentOther)
                    {
                        Debug.Log($"[Server] {clientName} is waiting for other receive solution...");
                        // Wait a little bit and then try again
                        await UniTask.Delay(HandleSendingDelay);
                        continue;
                    }

                    if (solutionSentClient && solutionSentOther)
                    {
                        connectedClientsMap.AddOrUpdate(
                            clientName,
                            key => throw new InvalidOperationException($"The client '{key}' was not found in the collection"),
                            (key, oldValue) => (client, string.Empty, false)
                        );

                        Debug.Log($"[Server] {clientName}: Both received the solution, so reset and start a new round...");

                        // Wait a little bit and then try again
                        await UniTask.Delay(HandleSendingDelay);
                        continue;
                    }

                    var winner = FindWinner(clientName, myMessage, otherClientName, otherClientMessage);
                    var gameMessage = string.Empty;


                    if (winner.Equals("Draw"))
                    {
                        gameMessage = "Thats a draw!";
                    }
                    else if (winner.Equals(clientName))
                    {
                        gameMessage = "You Win!";
                    }
                    else
                    {
                        gameMessage = "You Loose!";
                    }

                    Debug.Log($"[Server] {clientName} chose {myMessage} the other one chose {otherClientMessage}, so the solution is: {gameMessage}");


                    Debug.Log($"[Server] Send the solution to {clientName}");
                    await SendMessageAsync(new() { Code = MessageResponseCode.SOL, PlayerName = "Server", Text = gameMessage }, client, cancellationToken);

                    connectedClientsMap.AddOrUpdate(
                        clientName,
                        key => throw new InvalidOperationException($"The client '{key}' was not found in the collection"),
                        (key, oldValue) => (client, myMessage, true)
                    );

                    // Wait a little bit and then try again
                    await UniTask.Delay(HandleSendingDelay);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Server] Error handling messages for {clientName}: {ex.Message}");
            }
            finally
            {
                client.Close();
                Debug.Log($"[Server] Closed connection for {clientName}.");
            }
        }

        private string FindWinner(string client1Name, string message1, string client2Name, string message2)
            => (message1, message2) switch
            {
                ("Stein", "Schere") => client1Name,
                ("Schere", "Papier") => client1Name,
                ("Papier", "Stein") => client1Name,
                ("Schere", "Stein") => client2Name,
                ("Papier", "Schere") => client2Name,
                ("Stein", "Papier") => client2Name,
                _ when message1 == message2 => "Draw",
                _ => throw new ArgumentException("Invalid input")
            };

        private void HandleMessage(Socket handler, NetworkMessage message)
        {
            if (!connectedClientsMap.ContainsKey(message.PlayerName))
            {
                Debug.Log($"[Server] {message.PlayerName} is not known. This shouldn't be possible.");
                return;
            }

            if (message.Code == MessageResponseCode.END)
            {
                Debug.Log($"[Server] {message.PlayerName} wants to disconnect");
                connectedClientsMap.TryRemove(message.PlayerName, out var removedValue);
            }
            else if (message.Code == MessageResponseCode.MES)
            {
                Debug.Log($"[Server] {message.PlayerName} sent a message");
                connectedClientsMap.AddOrUpdate(
                    message.PlayerName,
                    key => (handler, message.Text, false),
                    (key, oldValue) => (handler, message.Text, oldValue.Item3)
                );

            }
        }
    }
}
