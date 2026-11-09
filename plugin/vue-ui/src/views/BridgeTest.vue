<script setup>
import { ref, computed } from 'vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
import { Badge } from '@/components/ui/badge';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { toast } from 'vue-sonner';
import {
  isBridgeAvailable,
  useBridgeService,
  useBridgeTransientService,
  useBridgeServiceEvent,
  getBridgeService,
  SERVICE_NAME,
  SESSION_SERVICE_NAME,
} from '@/composables/useDotnetBridge';
import HotKeyControl from '@/components/hotkey/HotKeyControl.vue';
import { useHotKeyCapture, HOT_KEY_CAPTURE_SERVICE } from '@/composables/useHotKeyCapture';

const bridgeReady = computed(() => isBridgeAvailable());
const vm = useBridgeService(SERVICE_NAME);

const sessionId = ref('session-alpha');
const sessionVm = useBridgeTransientService(SESSION_SERVICE_NAME, () =>
  bridgeReady.value ? [sessionId.value] : null
);

const echoInput = ref('Hello Bridge');
const eventInput = ref('测试事件');
const newItemName = ref('');
const eventLog = ref([]);
const methodLog = ref([]);
const demoHotKeyValueText = ref('0');
const demoHotKeyDisplay = ref('');
const captureVm = useBridgeService(HOT_KEY_CAPTURE_SERVICE);
const { beginCapture } = useHotKeyCapture();

function applyDemoHotKeyCapture() {
  const display = captureVm?.Methods?.CommitDraftDisplay?.() ?? '';
  demoHotKeyDisplay.value = display;
  demoHotKeyValueText.value = captureVm?.Properties?.DraftValueText ?? '0';
  toast.success('快捷键已保存（演示）', { description: display || '已清空' });
}

function beginDemoHotKeyCapture() {
  beginCapture(demoHotKeyValueText.value, '');
}

function logMethod(name, detail) {
  methodLog.value.unshift({
    time: new Date().toLocaleTimeString(),
    name,
    detail: detail ?? '',
  });
  if (methodLog.value.length > 50) {
    methodLog.value.length = 50;
  }
}

function logEvent(payload) {
  eventLog.value.unshift({
    time: new Date().toLocaleTimeString(),
    message: String(payload ?? ''),
  });
  if (eventLog.value.length > 50) {
    eventLog.value.length = 50;
  }
}

useBridgeServiceEvent(vm, 'MessageReceived', (payload) => {
  logEvent(payload);
  toast.info('收到 MessageReceived 事件', { description: String(payload) });
});

async function runMethod(name, fn) {
  try {
    const result = await fn();
    logMethod(name, result ?? 'OK');
    return result;
  } catch (err) {
    const message = err?.message ?? String(err);
    logMethod(name, `错误: ${message}`);
    toast.error(`${name} 失败`, { description: message });
    throw err;
  }
}

async function callIncrement() {
  await runMethod('Increment', () => vm.Methods.Increment());
}

async function callDecrement() {
  await runMethod('Decrement', () => vm.Methods.Decrement());
}

async function callReset() {
  await runMethod('Reset', () => vm.Methods.Reset());
}

async function callEcho() {
  const result = await runMethod('Echo', () => vm.Methods.Echo(echoInput.value));
  toast.success('Echo 成功', { description: String(result) });
}

async function callDelayedEcho() {
  const result = await runMethod('DelayedEcho', () =>
    vm.Methods.DelayedEcho(echoInput.value, 1500)
  );
  toast.success('DelayedEcho 成功', { description: String(result) });
}

async function callThrowError() {
  await runMethod('ThrowError', () => vm.Methods.ThrowError());
}

async function callFireEvent() {
  await runMethod('FireEvent', () => vm.Methods.FireEvent(eventInput.value));
}

async function callAddItem() {
  if (!newItemName.value.trim()) {
    toast.warning('请输入项目名称');
    return;
  }
  await runMethod('AddItem', () => vm.Methods.AddItem(newItemName.value.trim()));
  newItemName.value = '';
}

async function callRemoveItem(id) {
  await runMethod('RemoveItem', () => vm.Methods.RemoveItem(id));
}

async function callDirectGetService() {
  await runMethod('DirectGetService.Increment', async () => {
    const svc = await getBridgeService(SERVICE_NAME);
    return svc.Increment();
  });
}

async function callSessionPing() {
  if (!sessionVm?.Methods?.Ping) {
    toast.warning('Transient 服务未就绪');
    return;
  }
  const result = await runMethod('Session.Ping', () => sessionVm.Methods.Ping());
  toast.success('Ping 成功', { description: String(result) });
}
</script>

<template>
  <div class="space-y-6 p-6">
    <div>
      <h1 class="text-2xl font-bold text-foreground">Vue Bridge 测试</h1>
      <p class="mt-1 text-sm text-muted-foreground">
        服务名 <code class="rounded bg-muted px-1">{{ SERVICE_NAME }}</code>，
        Transient 服务 <code class="rounded bg-muted px-1">{{ SESSION_SERVICE_NAME }}</code>
      </p>
    </div>

    <!-- 环境状态 -->
    <Card>
      <CardHeader>
        <CardTitle>连接状态</CardTitle>
        <CardDescription>WebView2 与 DotnetBridge 环境检测</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-wrap gap-2">
        <Badge :variant="bridgeReady ? 'default' : 'secondary'">
          WebView2: {{ bridgeReady ? '已连接' : '未连接' }}
        </Badge>
        <Badge :variant="vm._loading ? 'secondary' : vm._error ? 'destructive' : 'default'">
          服务: {{ vm._loading ? '加载中' : vm._error ? '失败' : '已就绪' }}
        </Badge>
        <Badge v-if="sessionVm" :variant="sessionVm._loading ? 'secondary' : 'default'">
          Transient: {{ sessionVm._loading ? '加载中' : sessionVm._error ?? '已就绪' }}
        </Badge>
      </CardContent>
      <CardContent v-if="vm._error" class="pt-0 space-y-2">
        <p class="text-sm text-destructive">{{ vm._error }}</p>
        <ul class="list-inside list-disc text-xs text-muted-foreground">
          <li>注册 Singleton：<code class="rounded bg-muted px-1">{{ SERVICE_NAME }}</code></li>
          <li>注册 Transient：<code class="rounded bg-muted px-1">{{ SESSION_SERVICE_NAME }}</code></li>
          <li>服务名须与类名一致，参考 <code class="rounded bg-muted px-1">samples/csharp/BridgeTestService.cs</code></li>
          <li>metadata 越界：勿用 [RelayCommand]、勿用带默认值的参数</li>
        </ul>
      </CardContent>
      <CardContent v-if="sessionVm?._error" class="pt-0">
        <p class="text-sm text-destructive">Transient: {{ sessionVm._error }}</p>
      </CardContent>
    </Card>

    <!-- ObservableProperty -->
    <Card>
      <CardHeader>
        <CardTitle>ObservableProperty（useService）</CardTitle>
        <CardDescription>双向绑定 .NET 可观察属性</CardDescription>
      </CardHeader>
      <CardContent class="grid max-w-xl gap-4">
        <div class="space-y-2">
          <Label>Title</Label>
          <Input
            :model-value="vm.Properties.Title ?? ''"
            :disabled="!!vm._loading || !!vm._error"
            @update:model-value="(v) => vm.setProperty('Title', v)"
          />
        </div>
        <div class="space-y-2">
          <Label>Message</Label>
          <Input
            :model-value="vm.Properties.Message ?? ''"
            :disabled="!!vm._loading || !!vm._error"
            @update:model-value="(v) => vm.setProperty('Message', v)"
          />
        </div>
        <div class="flex items-center gap-3">
          <Switch
            :checked="!!vm.Properties.IsEnabled"
            :disabled="!!vm._loading || !!vm._error"
            @update:checked="(v) => vm.setProperty('IsEnabled', v)"
          />
          <Label>IsEnabled: {{ vm.Properties.IsEnabled }}</Label>
        </div>
        <p class="text-sm text-muted-foreground">
          Counter（只读展示）: <span class="font-mono text-foreground">{{ vm.Properties.Counter ?? 0 }}</span>
        </p>
      </CardContent>
    </Card>

    <!-- Methods -->
    <Card>
      <CardHeader>
        <CardTitle>Methods（useService.Methods）</CardTitle>
        <CardDescription>同步 / 异步 / 异常方法调用</CardDescription>
      </CardHeader>
      <CardContent class="space-y-4">
        <div class="flex flex-wrap gap-2">
          <Button :disabled="!!vm._loading || !!vm._error" @click="callIncrement">Increment</Button>
          <Button :disabled="!!vm._loading || !!vm._error" variant="secondary" @click="callDecrement">
            Decrement
          </Button>
          <Button :disabled="!!vm._loading || !!vm._error" variant="outline" @click="callReset">
            Reset
          </Button>
          <Button :disabled="!!vm._loading || !!vm._error" variant="outline" @click="callDirectGetService">
            DirectGetService
          </Button>
          <Button :disabled="!!vm._loading || !!vm._error" variant="destructive" @click="callThrowError">
            ThrowError
          </Button>
        </div>

        <div class="flex max-w-xl flex-wrap items-end gap-2">
          <div class="min-w-[200px] flex-1 space-y-2">
            <Label>Echo 输入</Label>
            <Input v-model="echoInput" />
          </div>
          <Button :disabled="!!vm._loading || !!vm._error" @click="callEcho">Echo</Button>
          <Button :disabled="!!vm._loading || !!vm._error" variant="secondary" @click="callDelayedEcho">
            DelayedEcho (1.5s)
          </Button>
        </div>

        <div class="flex max-w-xl flex-wrap items-end gap-2">
          <div class="min-w-[200px] flex-1 space-y-2">
            <Label>FireEvent 载荷</Label>
            <Input v-model="eventInput" />
          </div>
          <Button :disabled="!!vm._loading || !!vm._error" @click="callFireEvent">FireEvent</Button>
        </div>
      </CardContent>
    </Card>

    <!-- ObservableCollection -->
    <Card>
      <CardHeader>
        <CardTitle>ObservableCollection（useService.Collections）</CardTitle>
        <CardDescription>集合变更自动同步</CardDescription>
      </CardHeader>
      <CardContent class="space-y-4">
        <ul v-if="vm.Collections.Items?.length" class="space-y-1 text-sm">
          <li
            v-for="item in vm.Collections.Items"
            :key="item.Id ?? item.id"
            class="flex items-center justify-between rounded border border-border px-3 py-2"
          >
            <span>
              #{{ item.Id ?? item.id }} — {{ item.Name ?? item.name }}
            </span>
            <Button
              size="sm"
              variant="ghost"
              :disabled="!!vm._loading || !!vm._error"
              @click="callRemoveItem(item.Id ?? item.id)"
            >
              删除
            </Button>
          </li>
        </ul>
        <p v-else class="text-sm text-muted-foreground">暂无数据</p>

        <div class="flex max-w-md gap-2">
          <Input v-model="newItemName" placeholder="新项目名称" />
          <Button :disabled="!!vm._loading || !!vm._error" @click="callAddItem">AddItem</Button>
        </div>
      </CardContent>
    </Card>

    <!-- Transient Service -->
    <Card>
      <CardHeader>
        <CardTitle>Transient Service（useTransientService）</CardTitle>
        <CardDescription>构造参数变化时重建实例</CardDescription>
      </CardHeader>
      <CardContent class="space-y-4">
        <div class="flex max-w-md flex-wrap items-end gap-2">
          <div class="min-w-[200px] flex-1 space-y-2">
            <Label>SessionId</Label>
            <Input v-model="sessionId" />
          </div>
          <Button :disabled="!sessionVm || sessionVm._loading" @click="callSessionPing">Ping</Button>
        </div>
        <p v-if="sessionVm && !sessionVm._loading" class="text-sm text-muted-foreground">
          Label: {{ sessionVm.Properties.Label ?? '-' }} ·
          SessionId: {{ sessionVm.Properties.SessionId ?? sessionId }}
        </p>
        <p v-if="sessionVm?._error" class="text-sm text-destructive">{{ sessionVm._error }}</p>
      </CardContent>
    </Card>

    <Card>
      <CardHeader>
        <CardTitle>快捷键录入（HotKeyCaptureService）</CardTitle>
        <CardDescription>需在 WebView2 中测试；保存后仅本地演示</CardDescription>
      </CardHeader>
      <CardContent>
        <HotKeyControl
          :display="demoHotKeyDisplay"
          :baseline-value-text="demoHotKeyValueText"
          :on-begin-capture="beginDemoHotKeyCapture"
          :on-apply-capture="applyDemoHotKeyCapture"
        />
      </CardContent>
    </Card>

    <!-- Logs -->
    <div class="grid gap-6 lg:grid-cols-2">
      <Card>
        <CardHeader>
          <CardTitle>方法调用日志</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="max-h-64 space-y-2 overflow-y-auto font-mono text-xs">
            <p v-if="!methodLog.length" class="text-muted-foreground">暂无记录</p>
            <div
              v-for="(entry, i) in methodLog"
              :key="i"
              class="rounded border border-border px-2 py-1"
            >
              <span class="text-muted-foreground">{{ entry.time }}</span>
              <span class="ml-2 text-foreground">{{ entry.name }}</span>
              <span v-if="entry.detail" class="ml-2 text-muted-foreground">{{ entry.detail }}</span>
            </div>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>事件日志（MessageReceived）</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="max-h-64 space-y-2 overflow-y-auto font-mono text-xs">
            <p v-if="!eventLog.length" class="text-muted-foreground">暂无事件</p>
            <div
              v-for="(entry, i) in eventLog"
              :key="i"
              class="rounded border border-border px-2 py-1"
            >
              <span class="text-muted-foreground">{{ entry.time }}</span>
              <span class="ml-2 text-foreground">{{ entry.message }}</span>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </div>
</template>
