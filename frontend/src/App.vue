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
          <!-- Circle avatar icon like screenshots -->
          <div class="user-avatar">
            <svg viewBox="0 0 24 24" fill="none" width="22" height="22">
              <circle cx="12" cy="8" r="4" fill="#9ca3af"/>
              <path d="M4 20c0-4 3.6-7 8-7s8 3 8 7" stroke="#9ca3af" stroke-width="1.5" fill="none" stroke-linecap="round"/>
            </svg>
          </div>
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
          <router-link v-if="auth.canSelectShift" to="/shifts" class="nav-item" active-class="active">
            <span class="nav-icon">
              <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16"><path d="M10.707 2.293a1 1 0 00-1.414 0l-7 7a1 1 0 001.414 1.414L4 10.414V17a1 1 0 001 1h2a1 1 0 001-1v-2a1 1 0 011-1h2a1 1 0 011 1v2a1 1 0 001 1h2a1 1 0 001-1v-6.586l.293.293a1 1 0 001.414-1.414l-7-7z"/></svg>
            </span>
            Dashboard
          </router-link>

        </template>

        <!-- Management section -->
        <template v-if="auth.isAdmin || auth.canManageShifts">
          <div class="nav-section-label">Management</div>
          <router-link v-if="auth.isAdmin" to="/patients" class="nav-item" active-class="active">
            <span class="nav-icon">
              <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16"><path d="M13 6a3 3 0 11-6 0 3 3 0 016 0zM18 8a2 2 0 11-4 0 2 2 0 014 0zM14 15a4 4 0 00-8 0v1h8v-1zM6 8a2 2 0 11-4 0 2 2 0 014 0zM16 18v-1a5.972 5.972 0 00-.75-2.906A3.005 3.005 0 0119 15v1h-3zM4.75 14.094A5.973 5.973 0 004 17v1H1v-1a3 3 0 013.75-2.906z"/></svg>
            </span>
            Patient Management
          </router-link>
          <router-link v-if="auth.canManageShifts" to="/shift-management" class="nav-item" active-class="active">
            <span class="nav-icon">
              <svg viewBox="0 0 20 20" fill="currentColor" width="16" height="16"><path fill-rule="evenodd" d="M6 2a1 1 0 00-1 1v1H4a2 2 0 00-2 2v10a2 2 0 002 2h12a2 2 0 002-2V6a2 2 0 00-2-2h-1V3a1 1 0 10-2 0v1H7V3a1 1 0 00-1-1zm0 5a1 1 0 000 2h8a1 1 0 100-2H6z" clip-rule="evenodd"/></svg>
            </span>
            Shift Management
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
import { useRouter } from 'vue-router'
import { useAuthStore } from './store/auth'

const auth   = useAuthStore()
const router = useRouter()

function logout() {
  auth.logout()
  router.push('/login')
}

// roleLabel comes from DB via auth.roles store
</script>

<style scoped>
.app-sidebar { position: relative; min-height: calc(100vh - 60px); }
</style>