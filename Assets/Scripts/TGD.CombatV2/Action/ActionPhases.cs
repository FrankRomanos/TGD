namespace TGD.CombatV2
{
    public enum ActionPhase
    {
        W1_AimBegin,
        W1_AimCancel,
        W1_AimRejected,
        W2_ConfirmStart,
        W2_TargetInvalid,
        W2_PrecheckOk,
        W2_PreDeductCheckOk,
        W2_PreDeductCheckFail,
        W2_ConfirmAbort,
        W2_ChainPromptOpen,
        W2_ChainPromptAbort,
        W2_1_Select,
        W2_1_PreDeductCheckOk,
        W2_1_PreDeductCheckFail,
        W3_ExecuteBegin,
        W3_ExecuteEnd,
        W4_ResolveBegin,
        W4_ResolveEnd
    }
}