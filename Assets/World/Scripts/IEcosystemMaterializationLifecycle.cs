namespace Boreal.World
{
    // Live animal components implement this; EcosystemSimulation calls it before folding an animal back into its
    // population. It lives here so the two simulations never reference each other.
    public interface IEcosystemMaterializationLifecycle
    {
        void PrepareForAbstraction();
    }
}
