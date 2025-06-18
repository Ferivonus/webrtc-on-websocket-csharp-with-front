using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebSocketSignalR
{
    public class ChatHubRTC : Hub
    {
        private readonly ILogger<ChatHubRTC> _logger;

        // Bağlantı kimliği başına üye olduğu grupları tutar.
        private static readonly ConcurrentDictionary<string, HashSet<string>> UserGroups = new();

        public ChatHubRTC(ILogger<ChatHubRTC> logger)
        {
            _logger = logger;
        }

        public async Task SendSignal(string privateChatName, string username, string signalType, string signalData)
        {
            var connectionId = Context.ConnectionId;

            if (string.IsNullOrWhiteSpace(privateChatName) || string.IsNullOrWhiteSpace(signalType) || string.IsNullOrWhiteSpace(signalData))
            {
                await Clients.Caller.SendAsync("ReceiveError", "Signal data is invalid. Group name, signal type, or signal data cannot be empty.");
                return;
            }

            // Kullanıcının bu gruba katılma yetkisi olup olmadığını kontrol edin
            // (Bu örnekte JoinGroup metoduyla gruba katılmış olması yeterli kabul ediliyor)
            if (!UserGroups.TryGetValue(connectionId, out var groups) || !groups.Contains(privateChatName))
            {
                await Clients.Caller.SendAsync("ReceiveError", "You are not authorized to send signals to this group.");
                return;
            }

            // Sinyali, gönderen hariç gruptaki diğer tüm kullanıcılara gönder.
            await Clients.GroupExcept(privateChatName, connectionId).SendAsync("SignalReceived", new
            {
                Type = signalType,
                Data = signalData,
                From = username
            });

            _logger.LogInformation("Signal sent from {Username} (ConnectionId: {ConnectionId}) to group {Group}: Type={Type}", username, connectionId, privateChatName, signalType);
        }

        public async Task JoinGroup(string privateChatName, string username)
        {
            var connectionId = Context.ConnectionId;

            if (string.IsNullOrWhiteSpace(privateChatName) || string.IsNullOrWhiteSpace(username))
            {
                await Clients.Caller.SendAsync("JoinGroupError", "Group name or username cannot be empty.");
                return;
            }

            // Basit bir yetkilendirme kontrolü. Gerçek uygulamada daha sağlam bir kontrol olmalı.
            if (IsUserAuthorizedForGroup(username, privateChatName))
            {
                await Groups.AddToGroupAsync(connectionId, privateChatName);

                // Kullanıcının katıldığı grupları takip et
                UserGroups.AddOrUpdate(connectionId,
                    _ => new HashSet<string> { privateChatName },
                    (_, existingGroups) =>
                    {
                        existingGroups.Add(privateChatName);
                        return existingGroups;
                    });

                await Clients.Caller.SendAsync("JoinGroupSuccess", privateChatName);
                _logger.LogInformation("Client {ConnectionId} ({Username}) joined group {Group}", connectionId, username, privateChatName);

                // Gruptaki diğer üyelere yeni birinin katıldığını bildirebilirsiniz (isteğe bağlı)
                // await Clients.Group(privateChatName).SendAsync("UserJoined", username, connectionId);
            }
            else
            {
                await Clients.Caller.SendAsync("JoinGroupError", "Unauthorized to join this group.");
                _logger.LogWarning("Unauthorized join attempt for group {Group} by {Username} (ConnectionId: {ConnectionId})", privateChatName, username, connectionId);
            }
        }

        public async Task LeaveGroup(string privateChatName)
        {
            var connectionId = Context.ConnectionId;

            await Groups.RemoveFromGroupAsync(connectionId, privateChatName);

            // Kullanıcının gruptan ayrıldığını UserGroups'tan kaldırın
            if (UserGroups.TryGetValue(connectionId, out var groups))
            {
                groups.Remove(privateChatName);
                if (groups.Count == 0)
                    UserGroups.TryRemove(connectionId, out _);
            }

            await Clients.Caller.SendAsync("LeaveGroupSuccess", privateChatName);
            _logger.LogInformation("Client {ConnectionId} left group {Group}", connectionId, privateChatName);

            // Gruptaki diğer üyelere birinin ayrıldığını bildirebilirsiniz (isteğe bağlı)
            // await Clients.Group(privateChatName).SendAsync("UserLeft", connectionId);
        }

        public override async Task OnConnectedAsync()
        {
            var connectionId = Context.ConnectionId;
            _logger.LogInformation("Client connected: {ConnectionId}", connectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var connectionId = Context.ConnectionId;

            // Bağlantı koptuğunda kullanıcının tüm gruplarını temizle
            if (UserGroups.TryRemove(connectionId, out var groups))
            {
                foreach (var group in groups)
                {
                    await Groups.RemoveFromGroupAsync(connectionId, group);
                    _logger.LogInformation("Client {ConnectionId} automatically removed from group {Group} on disconnect.", connectionId, group);
                }
            }

            _logger.LogInformation("Client disconnected: {ConnectionId}. Exception: {Exception}", connectionId, exception?.Message);
            await base.OnDisconnectedAsync(exception);
        }

        // Bu metod, gerçek bir uygulamada daha karmaşık yetkilendirme mantığı içerecektir.
        // Örneğin, veritabanından kullanıcı rolleri veya grup davetiyeleri kontrol edilebilir.
        private bool IsUserAuthorizedForGroup(string username, string privateChatName)
        {
            // Şimdilik, sadece username ve privateChatName boş değilse yetkili sayıyoruz.
            // Gerçek senaryolarda bu, kullanıcı kimlik doğrulaması ve yetkilendirmeyi içermelidir.
            return !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(privateChatName);
        }
    }
}
