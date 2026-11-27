namespace DDEN.LoongNova
{
    internal static class MedicalMarket
    {
        internal static int InjectorValue(AmpouleState state) => state.Ready ? 40 : state.Spent ? 20 : 0;
    }
}
