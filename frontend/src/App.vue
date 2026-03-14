<template>
  <div v-if="auth.isLoggedIn" class="app-layout">

    <!-- ── Header ── logo left, user right, matches screenshots -->
    <header class="app-header">
      <div class="app-header-brand">
        <div class="dx7-logo">
          <span class="dx7-d">D</span><span class="dx7-x">X</span><span class="dx7-seven">7</span>
        </div>
      </div>

      <div class="app-header-right">
        <div class="app-header-user">
          <div class="user-info">
            <div class="user-name">{{ auth.user?.name }}</div>
            <div class="user-email">{{ auth.user?.email }}</div>
          </div>
          <!-- Dynamic avatar: photo or initials -->
          <img v-if="headerAvatarUrl" :src="headerAvatarUrl" class="header-avatar-img" :alt="auth.user?.name" @error="headerAvatarUrl = null" />
          <span v-else class="header-avatar-initials" :style="{ background: headerAvatarBg }">{{ headerInitials }}</span>
        </div>
        <button class="btn-pdf" @click="logout">Sign Out</button>
      </div>
    </header>

    <!-- ── Sidebar ── white, nav links with icon+label -->
    <aside class="app-sidebar">

      <nav style="padding-top:4px">
        <!-- Clinical section -->
        <template v-if="auth.isClinical || auth.isAdmin">
          <div class="nav-section-label">Clinical</div>
          <router-link v-if="auth.canSelectShift" to="/dashboard" class="nav-item" active-class="active">
            <span class="nav-icon">
              <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16"><path d="M10.707 2.293a1 1 0 00-1.414 0l-7 7a1 1 0 001.414 1.414L4 10.414V17a1 1 0 001 1h2a1 1 0 001-1v-2a1 1 0 011-1h2a1 1 0 011 1v2a1 1 0 001 1h2a1 1 0 001-1v-6.586l.293.293a1 1 0 001.414-1.414l-7-7z"/></svg>
            </span>
            Dashboard
          </router-link>

        </template>

        <!-- Management section -->
        <template v-if="auth.isAdmin">
          <div class="nav-section-label">Management</div>
          <router-link v-if="auth.isAdmin" to="/patients" class="nav-item" active-class="active">
            <span class="nav-icon">
              <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16"><path d="M13 6a3 3 0 11-6 0 3 3 0 016 0zM18 8a2 2 0 11-4 0 2 2 0 014 0zM14 15a4 4 0 00-8 0v1h8v-1zM6 8a2 2 0 11-4 0 2 2 0 014 0zM16 18v-1a5.972 5.972 0 00-.75-2.906A3.005 3.005 0 0119 15v1h-3zM4.75 14.094A5.973 5.973 0 004 17v1H1v-1a3 3 0 013.75-2.906z"/></svg>
            </span>
            Patient Management
          </router-link>
          <router-link v-if="auth.canManageUsers" to="/users" class="nav-item" active-class="active">
            <span class="nav-icon">
              <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16"><path fill-rule="evenodd" d="M10 9a3 3 0 100-6 3 3 0 000 6zm-7 9a7 7 0 1114 0H3z" clip-rule="evenodd"/></svg>
            </span>
            User Management
          </router-link>
          <router-link v-if="auth.canManageClinics" to="/clinics" class="nav-item" active-class="active">
            <span class="nav-icon">
              <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16"><path fill-rule="evenodd" d="M4 4a2 2 0 012-2h8a2 2 0 012 2v12a1 1 0 01-1 1H5a1 1 0 01-1-1V4zm3 1h2v2H7V5zm2 4H7v2h2V9zm2-4h2v2h-2V5zm2 4h-2v2h2V9z" clip-rule="evenodd"/></svg>
            </span>
            Client Management
          </router-link>
        </template>

        <!-- System section -->
        <template v-if="auth.isPlAdmin || auth.isClinicAdmin">
          <div class="nav-section-label">System</div>
          <router-link to="/hl7-inbox" class="nav-item" active-class="active">
            <span class="nav-icon">
              <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16"><path d="M2.003 5.884L10 9.882l7.997-3.998A2 2 0 0016 4H4a2 2 0 00-1.997 1.884z"/><path d="M18 8.118l-8 4-8-4V14a2 2 0 002 2h12a2 2 0 002-2V8.118z"/></svg>
            </span>
            HL7 Inbox
          </router-link>
        </template>
      </nav>

      <!-- Signed-in role badge at bottom -->
      <div class="sidebar-role-badge">
        <div class="sidebar-role-badge-inner">
          <div class="sidebar-role-label">Signed in as</div>
          <div class="sidebar-role-value">{{ auth.roleLabel }}</div>
        </div>
      </div>
    </aside>

    <!-- ── Main ── -->
    <main class="app-main">
      <router-view />
    </main>

  </div>

  <router-view v-else />
</template>

<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from './store/auth'
import api from './services/api'

const auth   = useAuthStore()
const router = useRouter()

function logout() {
  auth.logout()
  router.push('/login')
}

// ── Header avatar ──
const headerAvatarUrl = ref(null)

const avatarPalette = ['#1d4ed8','#0891b2','#059669','#7c3aed','#db2777','#d97706','#dc2626','#0284c7']

const headerAvatarBg = computed(() => {
  const name = auth.user?.name || ''
  if (!name) return '#6b7280'
  let hash = 0
  for (const ch of name) hash = (hash * 31 + ch.charCodeAt(0)) & 0xffffffff
  return avatarPalette[Math.abs(hash) % avatarPalette.length]
})

const headerInitials = computed(() => {
  const name = auth.user?.name || ''
  if (!name) return '?'
  const parts = name.trim().split(/\s+/)
  return parts.length >= 2
    ? (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
    : name.slice(0, 2).toUpperCase()
})

async function loadHeaderAvatar() {
  if (!auth.isLoggedIn) return
  try {
    const { data } = await api.get('/users/me')
    console.log('[avatar] /me response:', data.avatarUrl)
    headerAvatarUrl.value = data.avatarUrl ? data.avatarUrl + '?t=' + Date.now() : null
  } catch (e) {
    console.log('[avatar] /me error:', e.message)
    headerAvatarUrl.value = null
  }
}

onMounted(() => {
  loadHeaderAvatar()
  // retry once after a short delay in case token wasn't ready
  setTimeout(loadHeaderAvatar, 800)
})

watch(() => auth.isLoggedIn, (loggedIn) => {
  if (loggedIn) loadHeaderAvatar()
  else headerAvatarUrl.value = null
})
</script>

<style scoped>
.app-sidebar { position: relative; min-height: calc(100vh - 60px); }
</style>