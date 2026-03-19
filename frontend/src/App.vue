<template>
  <div v-if="auth.isLoggedIn" class="app-layout">

    <!-- ── Header ── logo left, user right, matches screenshots -->
    <header class="app-header">
      <div class="app-header-brand">
        <div class="dx7-logo">
          <span class="dx7-d">D</span><span class="dx7-x">X</span><span class="dx7-seven">7</span>
        </div>
      </div>

      <!-- Global quick search -->
      <div class="global-search-wrap" v-if="auth.isLoggedIn">
        <input
          v-model="searchQuery"
          class="global-search-input"
          placeholder="🔍 Search patients…"
          autocomplete="off"
          @input="onSearchInput"
          @keydown.escape="closeSearch"
          @blur="onSearchBlur"
        />
        <div v-if="searchResults.length" class="search-dropdown">
          <div
            v-for="p in searchResults"
            :key="p.id"
            class="search-result-item"
            @mousedown.prevent="goToPatient(p)"
          >
            <div class="sr-name">{{ p.name }}</div>
            <div class="sr-meta">{{ p.lisPatientId || '' }}</div>
          </div>
        </div>
        <div v-else-if="searchQuery.length >= 2 && !searching" class="search-dropdown">
          <div class="search-no-results">No patients found</div>
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
          <router-link to="/settings" class="nav-item" active-class="active">
            <span class="nav-icon">
              <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16"><path fill-rule="evenodd" d="M11.49 3.17c-.38-1.56-2.6-1.56-2.98 0a1.532 1.532 0 01-2.286.948c-1.372-.836-2.942.734-2.106 2.106.54.886.061 2.042-.947 2.287-1.561.379-1.561 2.6 0 2.978a1.532 1.532 0 01.947 2.287c-.836 1.372.734 2.942 2.106 2.106a1.532 1.532 0 012.287.947c.379 1.561 2.6 1.561 2.978 0a1.533 1.533 0 012.287-.947c1.372.836 2.942-.734 2.106-2.106a1.533 1.533 0 01.947-2.287c1.561-.379 1.561-2.6 0-2.978a1.532 1.532 0 01-.947-2.287c.836-1.372-.734-2.942-2.106-2.106a1.532 1.532 0 01-2.287-.947zM10 13a3 3 0 100-6 3 3 0 000 6z" clip-rule="evenodd"/></svg>
            </span>
            Settings
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

  <!-- Global custom dialog — mounted once, used everywhere -->
  <AppDialog />
</template>

<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from './store/auth'
import api from './services/api'
import { patientsApi } from './services/api'
import AppDialog from './components/AppDialog.vue'

const auth   = useAuthStore()
const router = useRouter()

function logout() {
  auth.logout()
  router.push('/login')
}

// ── Global quick search ──────────────────────────────────────────────────────
const searchQuery   = ref('')
const searchResults = ref([])
const searching     = ref(false)
let searchTimer     = null

async function onSearchInput() {
  clearTimeout(searchTimer)
  if (searchQuery.value.length < 2) { searchResults.value = []; return }
  searching.value = true
  searchTimer = setTimeout(async () => {
    try {
      const { data } = await patientsApi.getAll({ search: searchQuery.value, pageSize: 8 })
      searchResults.value = (data.data ?? data).slice(0, 8)
    } catch { searchResults.value = [] }
    finally { searching.value = false }
  }, 300)
}

function closeSearch() {
  searchQuery.value = ''
  searchResults.value = []
}

function onSearchBlur() {
  setTimeout(closeSearch, 150)
}

function goToPatient(p) {
  closeSearch()
  router.push(`/patients?highlight=${p.id}`)
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

/* Global search */
.global-search-wrap  { position: relative; flex: 1; max-width: 360px; margin: 0 16px; }
.global-search-input { width: 100%; height: 36px; padding: 0 12px; border: 1.5px solid rgba(255,255,255,0.2); border-radius: 20px; background: rgba(255,255,255,0.12); color: white; font-size: 13px; outline: none; }
.global-search-input::placeholder { color: rgba(255,255,255,0.5); }
.global-search-input:focus { border-color: rgba(255,255,255,0.5); background: rgba(255,255,255,0.18); }
.search-dropdown     { position: absolute; top: calc(100% + 6px); left: 0; right: 0; background: white; border: 1.5px solid var(--border); border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,0.12); z-index: 9999; overflow: hidden; }
.search-result-item  { padding: 10px 14px; cursor: pointer; display: flex; justify-content: space-between; align-items: center; }
.search-result-item:hover { background: #f0f9ff; }
.sr-name             { font-size: 13px; font-weight: 600; color: var(--navy); }
.sr-meta             { font-size: 11px; color: var(--slate); font-family: monospace; }
.search-no-results   { padding: 12px 14px; font-size: 13px; color: var(--slate); text-align: center; }
</style>