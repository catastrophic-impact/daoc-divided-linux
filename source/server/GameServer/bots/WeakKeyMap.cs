using System.Runtime.CompilerServices;

namespace DOL.GS
{
    /// <summary>
    /// A per-object map that does not keep its keys alive: an entry goes away with the bot or player it belongs to.
    /// For static per-bot / per-player state that is written often and never cleared (setup review 2026-11-06).
    /// Thread safe, as ConditionalWeakTable is.
    /// </summary>
    internal sealed class WeakKeyMap<TKey, TValue> where TKey : class
    {
        private readonly ConditionalWeakTable<TKey, StrongBox<TValue>> _table = new();

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (key != null && _table.TryGetValue(key, out StrongBox<TValue> box))
            {
                value = box.Value;
                return true;
            }

            value = default;
            return false;
        }

        public TValue this[TKey key]
        {
            set => _table.AddOrUpdate(key, new StrongBox<TValue>(value));
        }
    }
}
