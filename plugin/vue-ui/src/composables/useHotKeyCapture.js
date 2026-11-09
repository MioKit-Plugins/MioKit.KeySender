import { isBridgeAvailable, useBridgeService } from '@/composables/useDotnetBridge';

export const HOT_KEY_CAPTURE_SERVICE = 'HotKeyCaptureService';

export function useHotKeyCapture() {
  const captureVm = useBridgeService(HOT_KEY_CAPTURE_SERVICE);

  function beginCapture(baselineValueText, excludeOwnerId) {
    return captureVm?.Methods?.BeginCapture?.(
      baselineValueText ?? '0',
      excludeOwnerId ?? '',
    );
  }

  function cancelCapture() {
    return captureVm?.Methods?.CancelCapture?.();
  }

  function clearDraft() {
    return captureVm?.Methods?.ClearDraft?.();
  }

  return {
    captureVm,
    bridgeAvailable: isBridgeAvailable(),
    beginCapture,
    cancelCapture,
    clearDraft,
  };
}
