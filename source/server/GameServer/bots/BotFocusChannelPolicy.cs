namespace DOL.GS
{
    /// <summary>
    /// A damage focus (the Heretic's ramping fire) has no end of its own: it channels until the caster moves or casts
    /// something else. Discord 2026-11-09: a Heretic companion stayed in its channel while it and its group dropped to
    /// 1% health, because the bot AI waits out every running cast and a non-healer only looked at heals between casts.
    /// While a harmful focus channels, heals and cures are still checked; their cast replaces the channel.
    /// </summary>
    public static class BotFocusChannelPolicy
    {
        public static bool ChecksHealsDuringChannel(bool focusChanneling, Spell channeled) =>
            focusChanneling && channeled is { IsFocus: true, IsHarmful: true };

        public static bool ChecksHealsDuringChannel(CastingComponent casting) =>
            casting != null && ChecksHealsDuringChannel(casting.IsFocusChanneling, casting.SpellHandler?.Spell);
    }
}
