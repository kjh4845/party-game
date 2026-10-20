namespace ProjectHotfix.Contracts
{
    public readonly struct JumpPresentationState
    {
        public readonly float VerticalSpeed;
        public readonly bool Grounded;
        public readonly float LandingAge;
        public readonly float LandingImpactSpeed;
        public readonly float VelocityX;
        public readonly float VelocityZ;
        public readonly bool Sprinting;
        public readonly float GroundDistance;
        public readonly uint ResetVersion;

        public JumpPresentationState(float verticalSpeed, bool grounded, float landingAge, float landingImpactSpeed,
            float velocityX = 0f, float velocityZ = 0f, bool sprinting = false,
            float groundDistance = float.PositiveInfinity, uint resetVersion = 0)
        {
            VerticalSpeed = verticalSpeed;
            Grounded = grounded;
            LandingAge = landingAge;
            LandingImpactSpeed = landingImpactSpeed;
            VelocityX = velocityX;
            VelocityZ = velocityZ;
            Sprinting = sprinting;
            GroundDistance = groundDistance;
            ResetVersion = resetVersion;
        }
    }
}
