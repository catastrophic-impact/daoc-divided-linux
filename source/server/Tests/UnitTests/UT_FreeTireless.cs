using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.PropertyCalc;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture,NonParallelizable]
    public class UT_FreeTireless
    {
        private GameServer _previous;
        private PetTestLanguageScope _language;
        private double _modifier;
        private bool _recovery;
        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl=>DispatchProxy.Create<IObjectDatabase,UT_UnobservedConcentration.EmptyReads>();
        }
        private sealed class Human : GamePlayer
        {
            public bool Native;
            public override bool HasAbility(string key)=>key==DOL.GS.Abilities.Tireless && Native;
            private Human():base(null,null) { }
            public override bool IsAlive=>true;
            public override bool InCombat=>true;
            public override bool IsMoving=>true;
            public override bool IsSitting {get;set;}
        }
        private sealed class Bot : GameBot
        {
            private Bot():base((OfflineWorldBotRecord)null) { }
            public bool Native;
            public override bool HasAbility(string key)=>key==DOL.GS.Abilities.Tireless && Native;
            public override bool IsAlive=>true;
            public override bool InCombat=>true;
            public override bool IsMoving=>true;
            public override bool IsSitting {get;set;}
        }
        [SetUp]
        public void Setup()
        {
            _previous=GameServer.Instance;_language=new PetTestLanguageScope();
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            _modifier=DOL.GS.ServerProperties.Properties.ENDURANCE_REGEN_AMOUNT_MODIFIER;
            _recovery=DOL.GS.ServerProperties.Properties.FASTER_OUT_OF_COMBAT_RECOVERY;
            DOL.GS.ServerProperties.Properties.ENDURANCE_REGEN_AMOUNT_MODIFIER=1;
            DOL.GS.ServerProperties.Properties.FASTER_OUT_OF_COMBAT_RECOVERY=false;
        }
        [TearDown]
        public void Cleanup()
        {
            DOL.GS.ServerProperties.Properties.ENDURANCE_REGEN_AMOUNT_MODIFIER=_modifier;
            DOL.GS.ServerProperties.Properties.FASTER_OUT_OF_COMBAT_RECOVERY=_recovery;
            GameServer.LoadTestDouble(_previous);_language.Dispose();
        }
        [TestCase(false)]
        [TestCase(true)]
        public void NativeFreeTirelessWorksInCombatWhileMovingForPlayersAndBots(bool bot)
        {
            GameLiving actor=(GameLiving)RuntimeHelpers.GetUninitializedObject(bot?typeof(Bot):typeof(Human));
            foreach (var name in new[]{"BaseBuffBonusCategory","SpecBuffBonusCategory","AbilityBonus","ItemBonus"})
                typeof(GameLiving).GetField("<"+name+">k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,new PropertyIndexer());
            var abilities=new System.Collections.Generic.Dictionary<string,Ability>();
            typeof(GameLiving).GetField("m_abilities",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,abilities);
            typeof(GameLiving).GetField("_abilitiesLock",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor,new System.Threading.Lock());
            var calculator=new EnduranceRegenerationAmountCalculator();
            var property=eProperty.EnduranceRegenerationAmount;
            int without=calculator.CalcValue(actor,property);
            if (actor is Bot npc) npc.Native=true; else ((Human)actor).Native=true;
            Assert.That(calculator.CalcValue(actor,property),Is.EqualTo(without+1));
            actor.BaseBuffBonusCategory[property]=5;
            Assert.That(calculator.CalcValue(actor,property),Is.EqualTo(without+5));
            actor.AbilityBonus[property]=1;
            abilities["AtlasOF_Tireless"]=new DOL.GS.RealmAbilities.AtlasOF_RAEndRegenEnhancer(new DbAbility{KeyName="AtlasOF_Tireless",Name="Tireless"},1);
            Assert.That(calculator.CalcValue(actor,property),Is.EqualTo(without+5),"Previously purchased Tireless must not stack another point");
            actor.AbilityBonus[property]=5;   // the paid point plus the Champion Level 5 passive (+4), issue #10
            Assert.That(calculator.CalcValue(actor,property),Is.EqualTo(without+9),"The Champion passive stacks with buffs");
        }
        [TestCase(0,0,0,0,true,1)]
        [TestCase(0,1,0,0,true,1)]
        [TestCase(5,1,0,0,true,5)]
        [TestCase(5,1,0,0,false,6)]
        [TestCase(0,0,0,1,true,0)]
        public void TirelessFloorPreservesLegacyRulesWithoutGrant(double buffs,double ability,double items,int debuff,bool free,double expected)=>
            Assert.That(EnduranceRegenerationAmountCalculator.ActiveRegeneration(buffs,ability,items,debuff,free,(int)Math.Min(ability,1)),Is.EqualTo(expected));
        // Issue #10: the Champion Level 5 passive (+4 ability bonus) stacks with buffs, items and the free Tireless.
        [TestCase(0,4,0,0,0,5)]
        [TestCase(5,4,0,0,0,9)]
        [TestCase(5,5,0,0,1,9)]
        [TestCase(0,5,3,0,1,7)]
        [TestCase(5,4,0,2,0,7)]
        public void ChampionPassiveStacksWithFreeTireless(double buffs,double ability,double items,int debuff,int paid,double expected)=>
            Assert.That(EnduranceRegenerationAmountCalculator.ActiveRegeneration(buffs,ability,items,debuff,true,paid),Is.EqualTo(expected));
    }
}
