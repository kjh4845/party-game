namespace ProjectHotfix.Contracts
{
    public readonly struct GrabArmObstacle
    {
        public readonly float AX, AY, AZ, BX, BY, BZ, Radius;
        public GrabArmObstacle(float ax, float ay, float az, float bx, float by, float bz, float radius)
        { AX = ax; AY = ay; AZ = az; BX = bx; BY = by; BZ = bz; Radius = radius; }
    }
    public readonly struct GrabHandPresentationState
    {
        public readonly bool Active, Holding;
        public readonly float X, Y, Z, Weight, LiftAmount;
        public readonly float NormalX, NormalY, NormalZ;
        public readonly float SurfaceTop;
        public readonly GrabArmObstacle[] Obstacles;
        public GrabHandPresentationState(bool active, bool holding, float x, float y, float z, float weight, float liftAmount = 0f,
            float normalX = 0f, float normalY = 0f, float normalZ = 0f, GrabArmObstacle[] obstacles = null, float surfaceTop = float.NaN)
        { Active = active; Holding = holding; X = x; Y = y; Z = z; Weight = weight; LiftAmount = liftAmount;
          NormalX = normalX; NormalY = normalY; NormalZ = normalZ; Obstacles = obstacles; SurfaceTop = surfaceTop; }
    }
    public readonly struct GrabPresentationState
    {
        public readonly GrabHandPresentationState Left, Right;
        public readonly uint ResetVersion;
        public GrabPresentationState(GrabHandPresentationState left, GrabHandPresentationState right, uint resetVersion)
        { Left = left; Right = right; ResetVersion = resetVersion; }
    }
}
