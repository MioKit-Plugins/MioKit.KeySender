import { createRouter, createWebHashHistory } from 'vue-router';

const routes = [
  {
    path: '/',
    name: 'Manager',
    component: () => import('@/views/ManagerView.vue'),
  },
  {
    path: '/dev',
    component: () => import('@/layouts/AppLayout.vue'),
    redirect: '/dev/home',
    children: [
      {
        path: 'home',
        name: 'Home',
        component: () => import('@/views/Home.vue'),
      },
      {
        path: 'bridge-test',
        name: 'BridgeTest',
        component: () => import('@/views/BridgeTest.vue'),
      },
    ],
  },
  // Keep old paths as aliases so bookmarks/docs still work
  {
    path: '/home',
    redirect: '/dev/home',
  },
  {
    path: '/bridge-test',
    redirect: '/dev/bridge-test',
  },
];

const router = createRouter({
  history: createWebHashHistory(),
  routes,
});

if (typeof window !== 'undefined') {
  window.router = {
    push(path) {
      router.push(path);
    },
    replace(path) {
      router.replace(path);
    },
    back() {
      router.back();
    },
    current() {
      return router.currentRoute.value.path;
    },
  };
}

export default router;
