namespace ProjectHotfix.Contracts
{
    public readonly struct HandActionPresentationState
    {
        public readonly bool LeftPunch, RightPunch;
        public readonly float LeftAge, RightAge;
        public readonly uint ResetVersion;
        public readonly float PunchPlaybackRate;
        public HandActionPresentationState(bool leftPunch, float leftAge, bool rightPunch, float rightAge, uint resetVersion, float punchPlaybackRate = 1)
        { LeftPunch = leftPunch; LeftAge = leftAge; RightPunch = rightPunch; RightAge = rightAge; ResetVersion = resetVersion; PunchPlaybackRate = punchPlaybackRate; }
    }
}
