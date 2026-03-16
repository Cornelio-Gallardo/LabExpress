import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../store/auth'

const routes = [
  { path: '/login', name: 'Login', component: () => import('../views/LoginView.vue'), meta: { public: true } },
  { path: '/forgot-password', name: 'ForgotPassword', component: () => import('../views/ResetPasswordView.vue'), meta: { public: true } },
  { path: '/reset-password', name: 'ResetPassword', component: () => import('../views/ResetPasswordView.vue'), meta: { public: true } },
  { path: '/', redirect: '/dashboard' },
  { path: '/dashboard', name: 'Dashboard', component: () => import('../views/DashboardView.vue') },
  { path: '/roster', name: 'Roster', component: () => import('../views/PatientRosterView.vue') },
  { path: '/session/:sessionId', name: 'Session', component: () => import('../views/SessionView.vue') },
  { path: '/patients', name: 'Patients', component: () => import('../views/PatientsView.vue') },
  { path: '/users', name: 'Users', component: () => import('../views/UsersView.vue') },
  { path: '/clinics', name: 'Clinics', component: () => import('../views/ClinicsView.vue') },
  { path: '/hl7-inbox', name: 'Hl7Inbox', component: () => import('../views/Hl7InboxView.vue') },
  { path: '/longitudinal/:patientId', name: 'Longitudinal', component: () => import('../views/LongitudinalView.vue') },
  { path: '/settings', name: 'Settings', component: () => import('../views/SettingsView.vue') },
  { path: '/:pathMatch(.*)*', redirect: '/dashboard' }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  if (!to.meta.public && !auth.isLoggedIn) return '/login'
  if (to.path === '/login' && auth.isLoggedIn) return '/dashboard'
})

export default router