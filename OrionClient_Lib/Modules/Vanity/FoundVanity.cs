﻿namespace OrionClientLib.Modules.Vanity
{
    public class VanityTracker
    {
        public int VanityLength { get; set; }
        public List<FoundVanity> Vanities { get; private set; } = new List<FoundVanity>();
        private HashSet<string> _uniqueVanities = new HashSet<string>();


        public int Total => Vanities.Count;
        public int UniqueCount => _uniqueVanities.Count;
        public int Searching { get; set; }

        public int SessionCount { get; set; }
        public int SessionUniqueCount { get; set; }

        public void Add(FoundVanity vanity)
        {
            lock (Vanities)
            {
                Vanities.Add(vanity);
                _uniqueVanities.Add(vanity.VanityText);

                ++SessionCount;
                ++SessionUniqueCount;
            }
        }

        public void Reset()
        {
            SessionUniqueCount = 0;
            SessionCount = 0;
        }
    }

    public class FoundVanity
    {
        public string PublicKey { get; set; }
        public string PrivateKey { get; set; }
        public string VanityText { get; set; }
        public bool Exported { get; set; }

        public override int GetHashCode()
        {
            return VanityText?.GetHashCode() ?? base.GetHashCode();
        }

        public override bool Equals(object? obj)
        {
            if (obj is FoundVanity foundVanity)
            {
                return foundVanity.VanityText == VanityText;
            }

            return base.Equals(obj);
        }
    }
}
