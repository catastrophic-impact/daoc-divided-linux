using System;
using DOL.AI.Brain;
using DOL.Events;
using DOL.GS.Spells;
using DOL.GS;

namespace DOL.GS
{
    public class StormWolf : GameSummonedPet
    {
        public const double NormalDamage=2.0, CrownDamage=3.0;
        public const int HealthSteal=30;
        private ushort _normalModel;
        public StormWolf(INpcTemplate template):base(template) { DamageFactor=NormalDamage; }
        public override double MaxHealthScalingFactor => 4.0;
        public override void DealDamage(AttackData ad)
        {
            int healthBefore=ad.Target?.Health??0;
            base.DealDamage(ad);
            int dealt=Math.Min(healthBefore,Math.Max(0,ad.Damage+ad.CriticalDamage));
            if(dealt>0 && IsAlive) ChangeHealth(this,eHealthChangeType.Spell,StormforgedRules.RestorePerHit(dealt,HealthSteal));
            // The Tuatha's Faerie Drake: its breath (spells) marks what it hits for its owner.
            if(dealt>0 && ad.SpellHandler!=null && Owner is GamePlayer owner && PendragonRules.IsTuatha(owner.CharacterClass.ID))
                TuathaMarks.Add(owner,ad.Target,1);
        }
        public override double GetSpellScalingFactor() => Level / 50.0;
        public override void SortSpells()
        {
            base.SortSpells();
            if(HarmfulSpells!=null) HarmfulSpells=HarmfulSpells.ConvertAll(GetScaledSpell);
            if(InstantHarmfulSpells!=null) InstantHarmfulSpells=InstantHarmfulSpells.ConvertAll(GetScaledSpell);
        }
        public override bool SetPetLevel() { SummonSpellDamage=-100; SummonSpellValue=50; return base.SetPetLevel(); }
        public override bool AddToWorld()
        {
            if(!base.AddToWorld()) return false;
            _normalModel=Model;
            // Player-only event: a /spawn or gamebot owner threw here and the casting service removed the bot.
            // Bot owners are kept in sync by StormWolfBrain.Think instead.
            if(Owner is GamePlayer) GameEventMgr.AddHandler(Owner,GamePlayerEvent.StealthStateChanged,OwnerStealthChanged);
            SyncOwner();
            return true;
        }
        public override bool RemoveFromWorld()
        {
            if(Owner is GamePlayer) GameEventMgr.RemoveHandler(Owner,GamePlayerEvent.StealthStateChanged,OwnerStealthChanged);
            return base.RemoveFromWorld();
        }
        private void OwnerStealthChanged(DOLEvent e,object sender,EventArgs args)=>SyncOwner();
        public void SyncOwner()
        {
            if(Owner==null) return;
            SetPetLevel();
            Stealth(Owner.IsStealthed);
            bool empowered=StormforgedHeroicRules.HasAvatar(Owner);
            ushort model=empowered && PendragonRules.RealmOffset(Owner)==0?(ushort)650:_normalModel;
            if(Model!=model) Model=model;
            byte size=SizeFor(empowered);
            if(Size!=size) Size=size; // runs every brain tick: setting Size always broadcast a model+size packet
            DamageFactor=empowered?CrownDamage:NormalDamage;
            MaxSpeedBase = Owner.MaxSpeedBase;
        }
        public static byte SizeFor(bool empowered)=>empowered?(byte)70:(byte)50;
        public override void Stealth(bool hidden)
        {
            if(IsStealthed==hidden) return;
            if(hidden) { StopAttack(); StopCurrentSpellcast(); }
            Flags=hidden?Flags|eFlags.STEALTH:Flags&~eFlags.STEALTH;
            if(hidden && ObjectState==eObjectState.Active)
                foreach(var viewer in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                    if(!IsVisibleTo(viewer)) viewer.Out.SendObjectRemove(this);
        }
        public override bool IsVisibleTo(GameObject viewer) => base.IsVisibleTo(viewer) &&
            (!IsStealthed || viewer==Owner || viewer is GamePlayer player && Owner!=null && player.CanDetect(Owner));
    }
}

namespace DOL.AI.Brain
{
    public class StormWolfBrain : ControlledMobBrain
    {
        public StormWolfBrain(GameLiving owner):base(owner) { }
        public override void Think()
        {
            if(Body is StormWolf wolf) wolf.SyncOwner();
            if(Owner.IsStealthed)
            {
                Body.StopAttack(); ClearAggroList();
                if(WalkState==eWalkState.Follow) Body.Follow(Owner,MIN_OWNER_FOLLOW_DIST,MAX_OWNER_FOLLOW_DIST);
                return;
            }
            if(AggressionState!=eAggressionState.Passive && WalkState!=eWalkState.Stay && Owner.IsAttacking && Owner.TargetObject is GameLiving target &&
                GameServer.ServerRules.IsAllowedToAttack(Owner,target,true)) base.Attack(target);
            base.Think();
        }
        public override void Attack(GameObject target)
        {
            if(target is not GameLiving living || !GameServer.ServerRules.IsAllowedToAttack(Owner,living,true)) return;
            Owner.Stealth(false); Body.Stealth(false);
            base.Attack(target);
        }
    }
}

namespace DOL.GS.Spells
{
    [SpellHandler(eSpellType.StormSovereignWolf)]
    public class SummonStormWolf : SummonSpellHandler
    {
        public SummonStormWolf(GameLiving caster,Spell spell,SpellLine line):base(caster,spell,line) { }
        public override bool IsSummoningSpell=>true;
        public override string ShortDescription=>Spell.Description;
        // One wolf: a recast used to replace the owner's pet link and leave the old wolf in the world, so wolves piled up.
        public override bool CheckBeginCast(GameLiving selectedTarget,bool quiet)
        {
            if(Caster.ControlledBrain?.Body is { IsAlive: true, ObjectState: GameObject.eObjectState.Active } pet)
            {
                // The pet's own name: the Pendragon's Royal Lion and the Tuatha's Faerie Drake use this handler too.
                if(!quiet) MessageToCaster($"You already have a {pet.Name}. Release it first to call another.",DOL.GS.PacketHandler.eChatType.CT_SpellResisted);
                return false;
            }
            return base.CheckBeginCast(selectedTarget,quiet);
        }
        protected override IControlledBrain GetPetBrain(GameLiving owner)=>new StormWolfBrain(owner);
        protected override GameSummonedPet GetGamePet(INpcTemplate template)=>new StormWolf(template);
    }
}
