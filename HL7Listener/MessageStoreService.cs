namespace HL7Listener
{
    public class MessageStore
    {
        private readonly object _lock = new();
        private readonly Queue<string> _recent = new();
        public int TotalMessages { get; private set; }
        public DateTime LastReceived { get; private set; }

        public void AddMessage(string message)
        {
            lock (_lock)
            {
                TotalMessages++;
                LastReceived = DateTime.Now;
                _recent.Enqueue(message);
                if (_recent.Count > 5) _recent.Dequeue();
            }
        }

        public (int total, DateTime last, IEnumerable<string> recent) GetStats()
        {
            lock (_lock) return (TotalMessages, LastReceived, _recent.ToArray());
        }
    }
}
