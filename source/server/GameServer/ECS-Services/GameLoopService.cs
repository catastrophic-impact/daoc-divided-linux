namespace DOL.GS
{
    public sealed class GameLoopService : GameServiceBase
    {
        public static GameLoopService Instance { get; }

        static GameLoopService()
        {
            Instance = new();
        }

        public override void Tick()
        {
            ProcessPostedActionsParallel();
            ServerRules.RvrRealmPointRules.Tick(); // ended sieges pay their contribution pools (DR-182)
        }
    }
}
