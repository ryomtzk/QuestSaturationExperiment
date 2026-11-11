namespace QuestSaturation.Core
{
    /// <summary>
    /// オペレーターパネルを出してよい状態。被験者が見る Selection と Trial では決して出さない。
    /// ExperimentFlow は FreeControl に入ったときだけ ShowPanel を出すが、パネル側でもこの規則で確かめる。
    /// </summary>
    public static class PanelVisibility
    {
        public static bool Allowed(FlowState state) => state == FlowState.FreeControl;
    }
}
