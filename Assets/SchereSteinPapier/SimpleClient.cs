using Assets;
using Cysharp.Threading.Tasks;
using System;
using System.Net.Sockets;
using System.Threading;
using RockPaperScissors;
using RockPaperScissors.Assets;
using UnityEngine;

public sealed class SimpleClient : SimpleCommunicator
{
    private string? messageText;

    private CancellableTaskCollection serverListener = new();

    private bool receivedAck = true;

    private bool connected;

    public void SetMessage(string message)
    {
        messageText = message;
    }

    public override void Dispose()
    {
        serverListener.Dispose();

        base.Dispose();
    }

    protected override void Initialize()
    {
        // Nothing to initialize
    }

    protected override async UniTask RunAsync(CancellationToken cancellationToken)
    {
        connected = await ConnectToServerAsync(cancellationToken);

        if (!connected)
        {
            return;
        }

        serverListener.StartExecution(HandleServerMessages);
        serverListener.StartExecution(HandleSendMessages);
    }

    private async UniTask HandleSendMessages(CancellationToken cancellationToken)
    {
        while (connected && !cancellationToken.IsCancellationRequested)
        {
            await UniTask.WaitUntil(() => !string.IsNullOrEmpty(messageText) && receivedAck, cancellationToken: cancellationToken);

            var messageToSend = new NetworkMessage()
            {
                PlayerName = gameObject.name,
                Text = messageText,
                Code = MessageResponseCode.MES,
            };

            await SendMessageAsync(messageToSend, socket, cancellationToken);
            receivedAck = false;
        }
    }

    private async UniTask HandleServerMessages(CancellationToken cancellationToken)
    {
        Debug.Log($"{gameObject.name} starts listening to server messages...");

        connected = true;
        while (connected && !cancellationToken.IsCancellationRequested)
        {
            var response = await ReceiveResponseAsync(socket, cancellationToken);

            connected = ProcessResponse(response);

            if (response.Code == MessageResponseCode.ACK)
            {
                messageText = null;
                receivedAck = true;
                Debug.Log("Message was transmitted correctly, so reset message text to null");
            }
        }
    }

    private async UniTask<bool> ConnectToServerAsync(CancellationToken cancellationToken)
    {
        await socket.ConnectAsync(iPEndPoint);

        Debug.Log($"{gameObject.name} Connected to server");

        // Create Message
        var messageToSend = new NetworkMessage()
        {
            PlayerName = gameObject.name,
            Code = MessageResponseCode.CON,
        };

        await SendMessageAsync(messageToSend, socket, cancellationToken);

        var response = await ReceiveResponseAsync(socket, cancellationToken);

        return response.Code == MessageResponseCode.ACK;
    }
}