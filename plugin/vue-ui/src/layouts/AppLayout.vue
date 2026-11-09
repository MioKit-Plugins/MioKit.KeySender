<script setup>
import { computed } from 'vue';
import { useRoute, RouterLink } from 'vue-router';
import { LayoutDashboard, Cable, Keyboard } from 'lucide-vue-next';
import { cn } from '@/lib/utils';

const route = useRoute();

const navItems = [
  { path: '/', label: '管理器', icon: Keyboard },
  { path: '/dev/home', label: 'UI 组件', icon: LayoutDashboard },
  { path: '/dev/bridge-test', label: 'Bridge 测试', icon: Cable },
];

const activePath = computed(() => route.path);
</script>

<template>
  <div class="flex h-screen overflow-hidden bg-background">
    <aside class="flex h-full w-56 shrink-0 flex-col border-r border-border bg-card">
      <div class="border-b border-border px-4 py-5">
        <h1 class="text-base font-semibold text-foreground">MioKit UI</h1>
        <p class="mt-1 text-xs text-muted-foreground">WebView2 模板</p>
      </div>

      <nav class="flex flex-1 flex-col gap-1 p-3">
        <RouterLink
          v-for="item in navItems"
          :key="item.path"
          :to="item.path"
          :class="cn(
            'flex items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors',
            activePath === item.path
              ? 'bg-primary text-primary-foreground'
              : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'
          )"
        >
          <component :is="item.icon" class="size-4 shrink-0" />
          {{ item.label }}
        </RouterLink>
      </nav>
    </aside>

    <main class="min-h-0 min-w-0 flex-1 overflow-y-auto">
      <router-view />
    </main>
  </div>
</template>
