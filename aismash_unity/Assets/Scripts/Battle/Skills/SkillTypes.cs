namespace PromptFighters.Battle.Skills
{
    public enum SkillSlot { AttackA = 0, AttackB = 1, AttackC = 2, SmashSide = 3 }

    public enum Element { None, Physical, Fire, Ice, Lightning, Dark, Wind }

    public enum StatusType { None, Stun, Burn, Slow, GuardBreak }

    public enum RiskLevel { Low, Medium, High, Extreme }

    public static class SkillEnumParser
    {
        public static SkillSlot ParseSlot(string s) => s switch
        {
            "attack_a"   => SkillSlot.AttackA,
            "attack_b"   => SkillSlot.AttackB,
            "attack_c"   => SkillSlot.AttackC,
            "smash_side" => SkillSlot.SmashSide,
            "close"      => SkillSlot.AttackA,
            "ranged"     => SkillSlot.AttackB,
            "special"    => SkillSlot.AttackC,
            "ultimate"   => SkillSlot.SmashSide,
            _            => SkillSlot.AttackA,
        };

        public static Element ParseElement(string s) => s switch
        {
            "physical"  => Element.Physical,
            "fire"      => Element.Fire,
            "ice"       => Element.Ice,
            "lightning" => Element.Lightning,
            "dark"      => Element.Dark,
            "wind"      => Element.Wind,
            _           => Element.None,
        };

        public static StatusType ParseStatus(string s) => s switch
        {
            "stun"        => StatusType.Stun,
            "burn"        => StatusType.Burn,
            "slow"        => StatusType.Slow,
            "guard_break" => StatusType.GuardBreak,
            _             => StatusType.None,
        };

        public static UnityEngine.Color ElementColor(Element e) => e switch
        {
            Element.Fire      => new UnityEngine.Color(1f, 0.4f, 0.1f),
            Element.Ice       => new UnityEngine.Color(0.4f, 0.8f, 1f),
            Element.Lightning => new UnityEngine.Color(1f, 0.95f, 0.2f),
            Element.Dark      => new UnityEngine.Color(0.4f, 0.1f, 0.6f),
            Element.Wind      => new UnityEngine.Color(0.5f, 1f, 0.7f),
            Element.Physical  => new UnityEngine.Color(0.9f, 0.9f, 0.9f),
            _                 => UnityEngine.Color.white,
        };
    }
}
