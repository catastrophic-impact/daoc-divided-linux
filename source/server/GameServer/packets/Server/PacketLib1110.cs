using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DOL.GS.PacketHandler.Client.v168;
using DOL.GS.RealmAbilities;
using DOL.GS.Spells;
using DOL.GS.Styles;

namespace DOL.GS.PacketHandler
{
    [PacketLib(1110, GameClient.eClientVersion.Version1110)]
    public class PacketLib1110 : PacketLib1109
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Constructs a new PacketLib for Client Version 1.110
        /// </summary>
        /// <param name="client">the gameclient this lib is associated with</param>
        public PacketLib1110(GameClient client)
            : base(client)
        {
        }

        /// <summary>
        /// Property to enable "forced" Tooltip send when Update are made to player skills, or player effects.
        /// This can be controlled through server propertiers !
        /// </summary>
        public virtual bool ForceTooltipUpdate {
			get { return ServerProperties.Properties.USE_NEW_TOOLTIP_FORCEDUPDATE; }
		}

		/// <summary>
		/// The classic active-effect packet has separate fields for the visible
		/// icon and the tooltip identity.  Most stock spells intentionally use
		/// the same value for both, but the custom class spells reuse stock icons
		/// and have their own tooltip identities.  Sending the icon as the delve
		/// identity would show the stock spell's tooltip.  Keep the stock behavior
		/// for every other spell and substitute the spell's own tooltip identity
		/// only for the bounded custom spell ranges.
		/// </summary>
		private static ushort GetActiveEffectTooltipId(ECSGameEffect effect)
		{
			if (effect is ECSGameSpellEffect spellEffect)
			{
				var spell = spellEffect.SpellHandler?.Spell;
				if (spell != null && UsesOwnTooltipIdentity(spell.ID) && spell.InternalID != 0)
					return (ushort)spell.InternalID;
			}

			return effect.Icon;
		}

		/// <summary>
		/// Stormforged spells (62000-62239) have their own tooltips, as do and the offline Master Level and Champion abilities
		/// (63000-63199) with the stock effects they apply (ability ID * 1000 + n),
		/// and the Valkyrie test kit (64990-65150, tooltips 27000-27160; 65151+ are stock spells),
		/// and the Storm and Tuatha Sovereign remake styles, spells and regalia item abilities (63500-63860, 65600-65727) with their stock effects.
		/// Their icons are borrowed, so the icon's stock tooltip would be wrong.
		/// </summary>
		public static bool UsesOwnTooltipIdentity(int spellId)
		{
            if (DOL.GS.Spells.ModernMauler.Owns(spellId) || DOL.GS.Spells.ModernWarlock.Owns(spellId))
                return true;
			return spellId is >= 64000 and <= 64572 or >= 73000 and <= 73572 or >= 73000000 and <= 73572999 or >= 64990 and <= 65150 or >= 65200 and <= 65260 or >= 64000000 and <= 64572999 or >= 62000 and <= 62239 or >= 62400 and <= 62972 or >= 63000 and <= 63199 or >= 62400000 and <= 62960999 or >= 63000000 and <= 63199999 || IsStormRemake(spellId);
		}

		private static bool IsStormRemake(int spellId) =>
			spellId is >= DOL.GS.Spells.StormRemakeStyles.FirstEffectId and <= 63860 or >= DOL.GS.Spells.StormRemakeStyles.FirstEffectId * 1000 and <= 63860999 ||
			DOL.GS.Spells.TuathaRemakeSpells.Owns(spellId) || spellId is >= DOL.GS.Spells.TuathaRemakeSpells.FirstId * 1000 and <= 65727999 ||
			// Pendragon remake: style effects 63900-63960, spells and Valor icons 65800-65927, with their stock effects.
			spellId is >= DOL.GS.Spells.PendragonRemakeStyles.FirstEffectId and <= 63960 or >= DOL.GS.Spells.PendragonRemakeStyles.FirstEffectId * 1000 and <= 63960999 ||
			spellId is >= DOL.GS.Spells.PendragonRemakeSpells.FirstId and <= 65927 or >= DOL.GS.Spells.PendragonRemakeSpells.FirstId * 1000 and <= 65927999;

        /// <summary>
		/// New system in v1.110+ for delve info. delve is cached by client in extra file, stored locally.
		/// </summary>
		/// <param name="info"></param>
		public override void SendDelveInfo(string info)
		{
			using (var pak = PooledObjectFactory.GetForTick<GSTCPPacketOut>().Init(GetPacketCode(eServerPackets.DelveInfo)))
			{
				pak.WriteString(info, 2048);
				pak.WriteByte(0); // 0-terminated
				SendTCP(pak);
			}
		}

		public override void SendUpdateIcons(IList changedEffects, ref int lastUpdateEffectsCount)
		{
			if (m_gameClient.Player == null)
			{
				return;
			}

			using (var pak = PooledObjectFactory.GetForTick<GSTCPPacketOut>().Init(GetPacketCode(eServerPackets.UpdateIcons)))
			{
				long initPos = pak.Position;

				int fxcount = 0;
				int entriesCount = 0;

				pak.WriteByte(0); // effects count set in the end0
				pak.WriteByte(0); // unknown
				pak.WriteByte(Icons); // unknown
				pak.WriteByte(0); // unknown

				foreach (ECSGameEffect effect in m_gameClient.Player.effectListComponent.GetEffects().Where(e => e.EffectType != eEffect.Pulse))
				{
					if (effect.Icon == 0)
						continue;

					fxcount++;
					if (changedEffects != null && !changedEffects.Contains(effect))
					{
						continue;
					}

					// store tooltip update for gamespelleffect.
					if (ForceTooltipUpdate && effect is ECSGameSpellEffect gameEffect)
					{
						ISpellHandler spellHandler = gameEffect.SpellHandler;

						if (spellHandler.Spell.IsDynamic || UsesOwnTooltipIdentity(spellHandler.Spell.ID) || m_gameClient.CanSendTooltip(24, spellHandler.Spell.InternalID))
							SendDelveInfo(DetailDisplayHandler.DelveSpell(spellHandler));
					}

					// icon index
					pak.WriteByte((byte)(fxcount - 1));
					// Determines where to grab the icon from. Spell-based effect icons use a different source than Ability-based icons.
					pak.WriteByte((effect is ECSGameAbilityEffect && effect.Icon <= 5000) ? (byte)0xff : (byte)(fxcount - 1));
					//pak.WriteByte((effect is ECSGameSpellEffect || effect.Icon > 5000) ? (byte)(fxcount - 1) : (byte)0xff); // <- [Takii] previous version

					byte ImmunByte = 0;
					var gsp = effect as ECSGameEffect;
					if (gsp is ECSImmunityEffect || gsp.IsDisabled)
						ImmunByte = 1;
					//todo this should be the ImmunByte
					pak.WriteByte(ImmunByte); // new in 1.73; if non zero says "protected by" on right click

					// bit 0x08 adds "more..." to right click info
					pak.WriteShort(effect.Icon);
					pak.WriteShort((ushort)(effect.GetRemainingTimeForClient() / 1000));
					if (effect is ECSGameEffect || effect is ECSImmunityEffect)
						pak.WriteShort(GetActiveEffectTooltipId(effect)); // v1.110+ spell tooltip identity
					else
						pak.WriteShort(0);//don't override existing tooltip ids

					byte flagNegativeEffect = 0;

					if (!effect.HasPositiveEffect)
					{
						flagNegativeEffect = 1;
					}

					pak.WriteByte(flagNegativeEffect);

					pak.WritePascalString(effect.Name);
					entriesCount++;
				}

				int oldCount = lastUpdateEffectsCount;
				lastUpdateEffectsCount = fxcount;

				while (oldCount > fxcount)
				{
					pak.WriteByte((byte)(fxcount++));
					pak.Fill(0, 10);
					entriesCount++;
				}

				if (changedEffects != null)
				{
					changedEffects.Clear();
				}

				if (entriesCount == 0)
				{
					pak.ReleasePooledObject();
					return; // nothing changed - no update is needed
				}

				pak.Position = initPos;
				pak.WriteByte((byte)entriesCount);
				pak.Seek(0, SeekOrigin.End);

				SendTCP(pak);
			}
		}

		/// <summary>
		/// Override for handling force tooltip update...
		/// </summary>
		public override void SendTrainerWindow()
		{
			base.SendTrainerWindow();

			// Send tooltips
			if (ForceTooltipUpdate && m_gameClient.TrainerSkillCache != null)
				SendForceTooltipUpdate(m_gameClient.TrainerSkillCache.SelectMany(e => e.Item2).Select(e => e.Item3));
		}

		/// <summary>
		/// Send Delve for Provided Collection of Skills that need forced Tooltip Update.
		/// </summary>
		/// <param name="skills"></param>
		protected virtual void SendForceTooltipUpdate(IEnumerable<Skill> skills)
		{
			foreach (Skill t in skills)
			{
				if (t is Specialization)
					continue;

				if (t is RealmAbility)
				{
					if (m_gameClient.CanSendTooltip(27, t.InternalID))
						SendDelveInfo(DetailDisplayHandler.DelveRealmAbility(m_gameClient, t.InternalID));
				}
				else if (t is Ability)
				{
					if (m_gameClient.CanSendTooltip(28, t.InternalID))
						SendDelveInfo(DetailDisplayHandler.DelveAbility(m_gameClient, t.InternalID));
				}
				else if (t is Style style)
				{
					if (m_gameClient.CanSendTooltip(25, t.InternalID))
					{
						if (style.Procs != null && style.Procs.Count > 0)
						{
							foreach (StyleProcInfo proc in style.Procs)
								SendDelveInfo(DetailDisplayHandler.DelveSpell(m_gameClient, proc.Spell));
						}

						SendDelveInfo(DetailDisplayHandler.DelveStyle(m_gameClient, t.InternalID));
					}
				}
				else if (t is Spell spell)
				{
					if (spell is Song || spell.NeedInstrument)
					{
						if (m_gameClient.CanSendTooltip(26, spell.InternalID))
							SendDelveInfo(DetailDisplayHandler.DelveSong(m_gameClient, spell.InternalID));
					}

					if (m_gameClient.CanSendTooltip(24, spell.InternalID))
					{
						SendDelveInfo(DetailDisplayHandler.DelveSpell(m_gameClient, spell));

						if (spell.HasSubSpell)
						{
							// SubSpellID can be 0 when the secondaries come from MultipleSubSpells.
							foreach (int subSpellId in spell.MultipleSubSpells.Prepend(spell.SubSpellID))
							{
								Spell subSpell = subSpellId > 0 ? SkillBase.GetSpellByID(subSpellId) : null;

								if (subSpell != null && m_gameClient.CanSendTooltip(24, subSpell.InternalID))
									SendDelveInfo(DetailDisplayHandler.DelveSpell(m_gameClient, subSpell));
							}
						}

						if (spell.SpellType == eSpellType.DefensiveProc || spell.SpellType == eSpellType.OffensiveProc)
							SendDelveInfo(DetailDisplayHandler.DelveSpell(m_gameClient, SkillBase.GetSpellByID((int)spell.Value)));
					}
				}
			}
		}

		/// <summary>
		/// new siege weapon animation packet 1.110
		/// </summary>
		public override void SendSiegeWeaponAnimation(GameSiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return;
            using (var pak = PooledObjectFactory.GetForTick<GSTCPPacketOut>().Init(GetPacketCode(eServerPackets.SiegeWeaponAnimation)))
            {
                bool isGroundTargetValid = siegeWeapon.GroundTarget.IsValid;

                pak.WriteInt(siegeWeapon.ObjectID);
                pak.WriteInt(
                    (uint)
                    (siegeWeapon.TargetObject == null
                     ? (!isGroundTargetValid ? 0 : siegeWeapon.GroundTarget.X)
                     : siegeWeapon.TargetObject.X));
                pak.WriteInt(
                    (uint)
                    (siegeWeapon.TargetObject == null
                     ? (!isGroundTargetValid ? 0 : siegeWeapon.GroundTarget.Y)
                     : siegeWeapon.TargetObject.Y));
                pak.WriteInt(
                    (uint)
                    (siegeWeapon.TargetObject == null
                     ? (!isGroundTargetValid ? 0 : siegeWeapon.GroundTarget.Z)
                     : siegeWeapon.TargetObject.Z));
                pak.WriteInt((uint)(siegeWeapon.TargetObject == null ? 0 : siegeWeapon.TargetObject.ObjectID));
                pak.WriteShort(siegeWeapon.Effect);
                pak.WriteShort((ushort)(siegeWeapon.SiegeWeaponTimer.TimeUntilElapsed)); // timer is no longer ( value / 100 )
                pak.WriteByte((byte)siegeWeapon.SiegeWeaponTimer.CurrentAction);
                pak.Fill(0, 3); // TODO : these bytes change depending on siege weapon action, to implement when different ammo types available.
                SendTCP(pak);
            }
        }

		/// <summary>
		/// new siege weapon fireanimation 1.110 // patch 0021
		/// </summary>
		/// <param name="siegeWeapon">The siege weapon</param>
		/// <param name="timer">How long the animation lasts for</param>
		public override void SendSiegeWeaponFireAnimation(GameSiegeWeapon siegeWeapon, int timer)
		{
			if (siegeWeapon == null)
				return;
			using (var pak = PooledObjectFactory.GetForTick<GSTCPPacketOut>().Init(GetPacketCode(eServerPackets.SiegeWeaponAnimation)))
			{
				pak.WriteInt((uint) siegeWeapon.ObjectID);
				pak.WriteInt((uint) (siegeWeapon.TargetObject == null ? siegeWeapon.GroundTarget.X : siegeWeapon.TargetObject.X));
				pak.WriteInt((uint) (siegeWeapon.TargetObject == null ? siegeWeapon.GroundTarget.Y : siegeWeapon.TargetObject.Y));
				pak.WriteInt((uint) (siegeWeapon.TargetObject == null ? siegeWeapon.GroundTarget.Z + 50 : siegeWeapon.TargetObject.Z + 50));
				pak.WriteInt((uint) (siegeWeapon.TargetObject == null ? 0 : siegeWeapon.TargetObject.ObjectID));
				pak.WriteShort(siegeWeapon.Effect);
				pak.WriteShort((ushort) (timer)); // timer is no longer ( value / 100 )
				pak.WriteByte((byte) SiegeTimer.eAction.Fire);
				pak.WriteShort(0xE134); // default ammo type, the only type currently supported on DOL
				pak.WriteByte(0x08); // always this flag when firing
				SendTCP(pak);
			}
		}
    }
}
