using System.Collections.Concurrent;

namespace HL7Listener
{
    public class SseHub
    {
        private readonly ConcurrentDictionary<Guid, StreamWriter> _clients = new();

        public void Register(Guid id, StreamWriter writer) => _clients[id] = writer;
        public void Unregister(Guid id) => _clients.TryRemove(id, out _);

        public async Task BroadcastAsync(string data)
        {
            var dead = new List<Guid>();

            foreach (var (id, writer) in _clients)
            {
                try
                {
                    await writer.WriteAsync($"data: {data}\n\n");
                    await writer.FlushAsync();             //  ← important
                }
                catch
                {
                    dead.Add(id);
                }
            }

            // Clean up any closed connections
            foreach (var id in dead)
                _clients.TryRemove(id, out _);
        }
    }
}
