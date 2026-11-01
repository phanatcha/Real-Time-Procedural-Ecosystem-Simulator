namespace Boreal.Animals
{
    // Counts contiguous eligible-but-capped episodes, not frames or repeated attempts every frame.
    public sealed class BreedingWaitTracker
    {
        bool waiting;
        int revision = -1;
        public bool Observe(bool blocked, float deltaTime, int worldRevision)
        {
            if (deltaTime <= 0f) return false;
            if (revision != worldRevision) { waiting = false; revision = worldRevision; }
            bool entered = blocked && !waiting;
            waiting = blocked;
            return entered;
        }
    }
}
