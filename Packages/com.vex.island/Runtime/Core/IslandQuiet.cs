namespace Vex.Island
{
    public static class IslandQuiet
    {
        public const float Pill = 8f;
        public const float Bench = 20f;
        public const float Fade = 0.4f;
        public const float Appear = 0.18f;

        public enum Phase
        {
            Hidden,
            Appearing,
            Alive,
            Fading,
            Gone
        }

        public static float Wait(bool bench)
        {
            return bench ? Bench : Pill;
        }

        public static Phase Of(bool shown, float sinceShow, float idle, float wait, bool held)
        {
            if (!shown)
                return Phase.Hidden;
            if (sinceShow < Appear)
                return Phase.Appearing;
            if (held || idle < wait)
                return Phase.Alive;
            if (idle >= wait + Fade)
                return Phase.Gone;
            return Phase.Fading;
        }

        public static float Opacity(Phase phase, float sinceShow, float idle, float wait)
        {
            switch (phase)
            {
                case Phase.Appearing:
                    return Unit(sinceShow / Appear);
                case Phase.Alive:
                    return 1f;
                case Phase.Fading:
                    return 1f - Unit((idle - wait) / Fade);
                default:
                    return 0f;
            }
        }

        static float Unit(float t)
        {
            if (t <= 0f)
                return 0f;
            if (t >= 1f)
                return 1f;
            return t;
        }
    }
}
