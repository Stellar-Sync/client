using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace StellarSync.Services
{
    public class NetworkService : IDisposable
    {
        private ClientWebSocket? webSocket;
        private CancellationTokenSource? cancellationTokenSource;
        private bool isConnected = false;

        public event EventHandler<string>? MessageReceived;
        public event EventHandler? Connected;
        public event EventHandler? Disconnected;
        public event EventHandler<string>? ErrorOccurred;

        public bool IsConnected => isConnected;

        public async Task ConnectAsync(string serverUrl, string userName = "Unknown")
{
	try
	{
		webSocket = new ClientWebSocket();
		cancellationTokenSource = new CancellationTokenSource();

		// Convert HTTP URL to WebSocket URL
		string wsUrl = serverUrl.Replace("http://", "ws://").Replace("https://", "wss://");
		if (!wsUrl.EndsWith("/ws"))
		{
			wsUrl += "/ws";
		}

		await webSocket.ConnectAsync(new Uri(wsUrl), cancellationTokenSource.Token);
		
		isConnected = true;
		Connected?.Invoke(this, EventArgs.Empty);

		// Send connection message with user info
		var connectMessage = new
		{
			type = "connect",
			data = new
			{
				user_id = Guid.NewGuid().ToString(),
				name = userName
			}
		};

		await SendMessageAsync(JsonConvert.SerializeObject(connectMessage));

		// Start listening for messages
		_ = Task.Run(ReceiveMessagesAsync);
	}
	catch (Exception ex)
	{
		ErrorOccurred?.Invoke(this, ex.Message);
		throw;
	}
}

        public async Task DisconnectAsync()
        {
            if (webSocket != null && webSocket.State == WebSocketState.Open)
            {
                cancellationTokenSource?.Cancel();
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", CancellationToken.None);
            }

            isConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        public async Task SendMessageAsync(string message)
        {
            if (webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    var buffer = Encoding.UTF8.GetBytes(message);
                    await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, cancellationTokenSource?.Token ?? CancellationToken.None);
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, $"Failed to send message: {ex.Message}");
                    throw;
                }
            }
            else
            {
                var error = $"WebSocket is not open. State: {webSocket?.State}";
                ErrorOccurred?.Invoke(this, error);
                throw new InvalidOperationException(error);
            }
        }

        public async Task SendCharacterDataAsync(object characterData)
{
	if (!isConnected)
	{
		throw new InvalidOperationException("Not connected to server");
	}

	try
	{
		var message = new
		{
			type = "character_data",
			client = "stellar_sync",
			data = characterData
		};

		var jsonMessage = JsonConvert.SerializeObject(message);
		
		// Log message size for debugging
		var messageSize = Encoding.UTF8.GetByteCount(jsonMessage);
		System.Diagnostics.Debug.WriteLine($"Sending character data message, size: {messageSize} bytes");
		
		await SendMessageAsync(jsonMessage);
	}
	catch (Exception ex)
	{
		ErrorOccurred?.Invoke(this, $"Failed to send character data: {ex.Message}");
		throw;
	}
}

public async Task RequestUsersAsync()
{
	if (!isConnected)
	{
		throw new InvalidOperationException("Not connected to server");
	}

	try
	{
		var message = new
		{
			type = "request_users"
		};

		await SendMessageAsync(JsonConvert.SerializeObject(message));
	}
	catch (Exception ex)
	{
		ErrorOccurred?.Invoke(this, $"Failed to request users: {ex.Message}");
		throw;
	}
}

public async Task RequestUserDataAsync(string userId)
{
	if (!isConnected)
	{
		throw new InvalidOperationException("Not connected to server");
	}

	try
	{
		var message = new
		{
			type = "request_user_data",
			data = new
			{
				user_id = userId
			}
		};

		await SendMessageAsync(JsonConvert.SerializeObject(message));
	}
	catch (Exception ex)
	{
		ErrorOccurred?.Invoke(this, $"Failed to request user data: {ex.Message}");
		throw;
	}
}

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[4096];

            try
            {
                while (webSocket?.State == WebSocketState.Open && !cancellationTokenSource?.Token.IsCancellationRequested == true)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationTokenSource?.Token ?? CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        MessageReceived?.Invoke(this, message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        isConnected = false;
                        Disconnected?.Invoke(this, EventArgs.Empty);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex.Message);
            }
        }

        public void Dispose()
        {
            cancellationTokenSource?.Cancel();
            webSocket?.Dispose();
            cancellationTokenSource?.Dispose();
        }
    }
}


