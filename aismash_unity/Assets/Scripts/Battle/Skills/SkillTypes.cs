namespace PromptFighters.Battle.Skills
{
    public enum SkillSlot { Close = 0, Ranged = 1, Special = 2, Ultimate = 3 }

    public enum Element { None, Physical, Fire, Ice, Lightning, Dark, Wind }

    public enum StatusType { None, Stun, Burn, Slow, GuardBreak }

    public enum RiskLevel { Low, Medium, High, Extreme }

    public static class SkillEnumParser
    {
        public static SkillSlot ParseSlot(string s) => s switch
        {
            "close"    => SkillSlot.Close,
            "ranged"   => SkillSlot.Ranged,
            "special"  => SkillSlot.Special,
            "ultimate" => SkillSlot.Ultimate,
            _          => SkillSlot.Close,
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
