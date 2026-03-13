<template>
  <div class="login-page">
    <div class="login-box">
      <div class="login-logo">
        <div class="login-logo-icon">Dx7</div>
        <div class="login-logo-name">Dx7</div>
      </div>

      <!-- Step 1: Enter email to request reset -->
      <div v-if="step === 'request'">
        <div style="font-size:20px;font-weight:700;color:var(--navy);font-family:'Plus Jakarta Sans',sans-serif">Forgot password?</div>
        <div class="login-sub" style="margin-bottom:24px">Enter your email and we'll send you a reset link.</div>

        <div v-if="error" class="login-error">{{ error }}</div>
        <div v-if="success" style="background:#dcfce7;border:1px solid #86efac;color:#166534;padding:12px;border-radius:8px;font-size:13px;margin-bottom:14px">
          {{ success }}
        </div>

        <div class="form-group">
          <label class="form-label">Email Address</label>
          <input v-model="email" type="email" class="login-input" placeholder="you@clinic.com" @keyup.enter="requestReset" />
        </div>
        <button class="login-btn" @click="requestReset" :disabled="loading">
          {{ loading ? 'Sending...' : 'Send Reset Link' }}
        </button>
        <div style="text-align:center;margin-top:16px">
          <router-link to="/login" style="font-size:13px;color:var(--primary)">← Back to login</router-link>
        </div>
      </div>

      <!-- Step 2: Enter new password -->
      <div v-else-if="step === 'reset'">
        <div style="font-size:20px;font-weight:700;color:var(--navy);font-family:'Plus Jakarta Sans',sans-serif">Set new password</div>
        <div class="login-sub" style="margin-bottom:24px">Choose a strong password for <strong>{{ email }}</strong></div>

        <div v-if="error" class="login-error">{{ error }}</div>

        <div class="form-group">
          <label class="form-label">New Password</label>
          <input v-model="newPassword" type="password" class="login-input" placeholder="At least 8 characters" />
        </div>
        <div class="form-group">
          <label class="form-label">Confirm Password</label>
          <input v-model="confirmPassword" type="password" class="login-input" placeholder="Repeat password" @keyup.enter="doReset" />
        </div>

        <!-- Password strength bar -->
        <div style="margin-bottom:16px">
          <div style="height:4px;background:#e2e8f0;border-radius:2px;overflow:hidden">
            <div :style="{width: strength.pct + '%', background: strength.color, height:'100%', transition:'all 0.2s'}"></div>
          </div>
          <div style="font-size:11px;color:var(--slate);margin-top:4px">{{ strength.label }}</div>
        </div>

        <button class="login-btn" @click="doReset" :disabled="loading">
          {{ loading ? 'Saving...' : 'Reset Password' }}
        </button>
      </div>

      <!-- Step 3: Done -->
      <div v-else-if="step === 'done'" style="text-align:center;padding:16px 0">
        <div style="font-size:40px;margin-bottom:12px">✅</div>
        <div style="font-size:18px;font-weight:700;color:var(--navy);margin-bottom:8px">Password updated!</div>
        <div style="font-size:13px;color:var(--slate);margin-bottom:24px">You can now log in with your new password.</div>
        <router-link to="/login" class="login-btn" style="display:block;text-align:center;text-decoration:none">Go to Login</router-link>
      </div>

      <div class="login-footer">Dx7 Clinical Information System · LABExpress</div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import api from '../services/api'

const route = useRoute()
const step          = ref('request')
const email         = ref('')
const newPassword   = ref('')
const confirmPassword = ref('')
const token         = ref('')
const loading       = ref(false)
const error         = ref('')
const success       = ref('')

onMounted(() => {
  // If token + email in URL, go straight to reset step
  if (route.query.token && route.query.email) {
    token.value = route.query.token
    email.value = decodeURIComponent(route.query.email)
    step.value  = 'reset'
  }
})

const strength = computed(() => {
  const p = newPassword.value
  if (!p) return { pct: 0, color: '#e2e8f0', label: '' }
  let score = 0
  if (p.length >= 8)  score++
  if (p.length >= 12) score++
  if (/[A-Z]/.test(p)) score++
  if (/[0-9]/.test(p)) score++
  if (/[^A-Za-z0-9]/.test(p)) score++
  const map = [
    { pct: 20, color: '#ef4444', label: 'Too weak' },
    { pct: 40, color: '#f97316', label: 'Weak' },
    { pct: 60, color: '#eab308', label: 'Fair' },
    { pct: 80, color: '#22c55e', label: 'Strong' },
    { pct: 100, color: '#16a34a', label: 'Very strong' },
  ]
  return map[Math.min(score, 4)]
})

async function requestReset() {
  error.value   = ''
  success.value = ''
  if (!email.value) { error.value = 'Please enter your email'; return }
  loading.value = true
  try {
    const { data } = await api.post('/auth/forgot-password', { email: email.value })
    success.value = data.message
    // In dev, show the link directly
    if (data.devLink) {
      success.value += '\n\nDev mode — no SMTP configured. Reset link: ' + data.devLink
    }
  } catch (e) {
    error.value = e.response?.data?.message || 'Failed to send reset link'
  } finally { loading.value = false }
}

async function doReset() {
  error.value = ''
  if (!newPassword.value || newPassword.value.length < 8) {
    error.value = 'Password must be at least 8 characters'
    return
  }
  if (newPassword.value !== confirmPassword.value) {
    error.value = 'Passwords do not match'
    return
  }
  loading.value = true
  try {
    await api.post('/auth/reset-password', {
      email:       email.value,
      token:       token.value,
      newPassword: newPassword.value
    })
    step.value = 'done'
  } catch (e) {
    error.value = e.response?.data?.message || 'Reset failed. The link may have expired.'
  } finally { loading.value = false }
}
</script>