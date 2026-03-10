namespace UiShowcaseCoreMod.Showcase;

public sealed record ReactiveShowcaseState(int Counter, string ThemeClass, string DensityClass, bool CheckboxChecked, bool SwitchEnabled, bool FormError, string FormStatus, int SelectedItem, int SelectedMode, bool ModalOpen, bool ToastVisible);
