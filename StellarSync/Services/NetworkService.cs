using System;
using System.Collections.Generic;
using System.Linq;
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
        private string currentUserId = string.Empty;

        public event EventHandler<string>? MessageReceived;
        public event EventHandler? Connected;
        public event EventHandler? Disconnected;
        public event EventHandler<string>? ErrorOccurred;

        public bool IsConnected => isConnected;

        public async Task ConnectAsync(string serverUrl, string userName = "Unknown", string zone = "")
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

		// Generate and store user ID
		currentUserId = Guid.NewGuid().ToString();
		
		// Send connection message with user info
		var connectMessage = new
		{
			type = "connect",
			data = new
			{
				user_id = currentUserId,
				name = userName,
				zone = zone // Add zone information if provided
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
        
        public string GetCurrentUserId()
        {
            return currentUserId;
        }



        public async Task SendMessageAsync(string message)
        {
            // Check both isConnected flag and actual WebSocket state
            if (!isConnected || webSocket?.State != WebSocketState.Open)
            {
                var error = $"WebSocket is not connected. isConnected: {isConnected}, State: {webSocket?.State}";
                System.Diagnostics.Debug.WriteLine($"ERROR: {error}");
                ErrorOccurred?.Invoke(this, error);
                throw new InvalidOperationException(error);
            }

            try
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                var messageSize = buffer.Length;
                
                // Log message size for debugging
                System.Diagnostics.Debug.WriteLine($"DEBUG: Sending message of size: {messageSize} bytes ({messageSize / (1024.0 * 1024.0):F2} MB)");
                
                // Check if message is too large (over 10MB)
                if (messageSize > 10 * 1024 * 1024)
                {
                    System.Diagnostics.Debug.WriteLine($"WARNING: Large message detected ({messageSize / (1024.0 * 1024.0):F2} MB), this may cause connection issues");
                }
                
                // Send the message
                await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, cancellationTokenSource?.Token ?? CancellationToken.None);
                
                System.Diagnostics.Debug.WriteLine($"DEBUG: Message sent successfully");
            }
            catch (Exception ex)
            {
                // If sending fails, mark as disconnected
                isConnected = false;
                var errorMessage = $"Failed to send message: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"ERROR: {errorMessage}");
                ErrorOccurred?.Invoke(this, errorMessage);
                Disconnected?.Invoke(this, EventArgs.Empty);
                throw;
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
		// Convert byte arrays to base64 strings for JSON serialization
		var serializableData = ConvertCharacterDataForSerialization(characterData);
		
		var message = new
		{
			type = "character_data",
			client = "stellar_sync",
			data = serializableData
		};

		var jsonMessage = JsonConvert.SerializeObject(message);
		
		// Log message size for debugging
		var messageSize = Encoding.UTF8.GetByteCount(jsonMessage);
		System.Diagnostics.Debug.WriteLine($"Sending character data message, size: {messageSize} bytes ({messageSize / (1024.0 * 1024.0):F2} MB)");
		
		// Check if message is too large
		if (messageSize > 1024 * 1024) // 1MB
		{
			System.Diagnostics.Debug.WriteLine($"WARNING: Large message detected ({messageSize / (1024.0 * 1024.0):F2} MB) - this may cause issues");
		}
		
		// Debug: Check if message contains problematic characters
		if (jsonMessage.Contains("\\"))
		{
			System.Diagnostics.Debug.WriteLine($"WARNING: Message contains backslashes - this may cause JSON parsing issues");
		}
		
		// Debug: Log what's actually being sent
		System.Diagnostics.Debug.WriteLine($"DEBUG: Message structure - type: {message.type}, client: {message.client}");
		if (serializableData is Dictionary<string, object> dataDict)
		{
			foreach (var kvp in dataDict)
			{
				if (kvp.Key == "PenumbraFiles" && kvp.Value is Dictionary<string, string> files)
				{
					System.Diagnostics.Debug.WriteLine($"DEBUG: PenumbraFiles count: {files.Count}");
					foreach (var file in files.Take(3)) // Log first 3 files
					{
						System.Diagnostics.Debug.WriteLine($"DEBUG: File {file.Key}: {file.Value.Length} base64 chars");
					}
				}
			}
		}
		
		await SendMessageAsync(jsonMessage);
	}
	catch (Exception ex)
	{
		ErrorOccurred?.Invoke(this, $"Failed to send character data: {ex.Message}");
		throw;
	}
}

private object ConvertCharacterDataForSerialization(object characterData)
{
	try
	{
		// Use reflection to convert byte arrays to base64 strings
		var dataType = characterData.GetType();
		var properties = dataType.GetProperties();
		
		System.Diagnostics.Debug.WriteLine($"DEBUG: Converting {dataType.Name} with {properties.Length} properties");
		
		// Create a dictionary to hold the serializable data
		var serializableData = new Dictionary<string, object>();
		
		foreach (var property in properties)
		{
			var value = property.GetValue(characterData);
			System.Diagnostics.Debug.WriteLine($"DEBUG: Property {property.Name}: {value?.GetType().Name ?? "null"}");
			
			if (property.Name == "PenumbraFileMetadata" && value is Dictionary<string, object> penumbraFileMetadata)
			{
				System.Diagnostics.Debug.WriteLine($"DEBUG: Found PenumbraFileMetadata with {penumbraFileMetadata.Count} entries");
				// File metadata is already JSON-serializable, no conversion needed
				serializableData[property.Name] = penumbraFileMetadata;
				System.Diagnostics.Debug.WriteLine($"DEBUG: Added PenumbraFileMetadata with {penumbraFileMetadata.Count} entries");
			}
			else if (property.Name == "PenumbraFileMetadata")
			{
				System.Diagnostics.Debug.WriteLine($"DEBUG: PenumbraFileMetadata property found but value is {value?.GetType().Name ?? "null"}");
				// Keep as-is for now
				serializableData[property.Name] = value;
			}
			else
			{
				// Keep other properties as-is
				serializableData[property.Name] = value;
			}
		}
		
		System.Diagnostics.Debug.WriteLine($"DEBUG: Serialization complete, returning {serializableData.Count} properties");
		
		// Debug: Check if penumbra_file_metadata has any problematic characters
		if (serializableData.ContainsKey("PenumbraFileMetadata") && serializableData["PenumbraFileMetadata"] is Dictionary<string, object> metadata)
		{
			System.Diagnostics.Debug.WriteLine($"DEBUG: PenumbraFileMetadata contains {metadata.Count} entries");
			var sampleEntry = metadata.FirstOrDefault();
			if (!sampleEntry.Equals(default(KeyValuePair<string, object>)))
			{
				System.Diagnostics.Debug.WriteLine($"DEBUG: Sample metadata key: {sampleEntry.Key}");
				if (sampleEntry.Value is Dictionary<string, object> fileMetadata)
				{
					foreach (var kvp in fileMetadata)
					{
						System.Diagnostics.Debug.WriteLine($"DEBUG: File metadata field '{kvp.Key}': {kvp.Value}");
					}
				}
			}
		}
		
		return serializableData;
	}
	catch (Exception ex)
	{
		System.Diagnostics.Debug.WriteLine($"Error converting character data for serialization: {ex.Message}");
		return characterData; // Fallback to original data
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
		var error = "Not connected to server";
		System.Diagnostics.Debug.WriteLine($"ERROR: {error}");
		throw new InvalidOperationException(error);
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

		var jsonMessage = JsonConvert.SerializeObject(message);
		System.Diagnostics.Debug.WriteLine($"DEBUG: Requesting user data for user ID: {userId}");
		System.Diagnostics.Debug.WriteLine($"DEBUG: Message to send: {jsonMessage}");
		
		await SendMessageAsync(jsonMessage);
		System.Diagnostics.Debug.WriteLine($"DEBUG: User data request sent successfully for user ID: {userId}");
	}
	catch (Exception ex)
	{
		var errorMessage = $"Failed to request user data for user {userId}: {ex.Message}";
		System.Diagnostics.Debug.WriteLine($"ERROR: {errorMessage}");
		ErrorOccurred?.Invoke(this, errorMessage);
		throw;
	}
}

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[4096];
            var messageBuffer = new List<byte>();

            try
            {
                while (webSocket?.State == WebSocketState.Open && !cancellationTokenSource?.Token.IsCancellationRequested == true)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationTokenSource?.Token ?? CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        // Add received data to message buffer
                        messageBuffer.AddRange(buffer.Take(result.Count));

                        // Check if this is the end of the message
                        if (result.EndOfMessage)
                        {
                            // Convert complete message to string
                            var completeMessage = Encoding.UTF8.GetString(messageBuffer.ToArray());
                            System.Diagnostics.Debug.WriteLine($"DEBUG: Received complete message of {completeMessage.Length} characters");
                            
                            // Clear buffer for next message
                            messageBuffer.Clear();
                            
                            // Process the complete message
                            MessageReceived?.Invoke(this, completeMessage);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"DEBUG: Received partial message frame of {result.Count} bytes");
                        }
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
                // CRITICAL FIX: Set isConnected to false when connection fails
                isConnected = false;
                ErrorOccurred?.Invoke(this, ex.Message);
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task SendNameUpdateAsync(string newName)
        {
            if (!isConnected)
            {
                throw new InvalidOperationException("Not connected to server");
            }

            try
            {
                var message = new
                {
                    type = "name_update",
                    data = new
                    {
                        name = newName
                    }
                };

                await SendMessageAsync(JsonConvert.SerializeObject(message));
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Failed to send name update: {ex.Message}");
                throw;
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


