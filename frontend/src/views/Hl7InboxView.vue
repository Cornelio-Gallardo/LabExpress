<template>
  <div>
    <div class="page-header">
      <div class="flex-between">
        <div>
          <div class="page-title">HL7 Inbox</div>
          <div class="page-sub">Processed lab order files from LIS / Sysmex</div>
        </div>
        <div class="flex gap-2">
          <button class="btn btn-outline" @click="load" :disabled="loading">
            {{ loading ? 'Refreshing...' : '↺ Refresh' }}
          </button>
          <button class="btn btn-outline" :class="{ 'btn-danger-outline': quarantineFiles.length }" @click="showQuarantine=!showQuarantine">
            🔴 Quarantine{{ quarantineFiles.length ? ` (${quarantineFiles.length})` : '' }}
          </button>

        </div>
      </div>
    </div>

    <!-- Stat cards -->
    <div class="stat-grid" style="margin-bottom:20px">
      <div class="stat-card">
        <div class="stat-icon">📥</div>
        <div class="stat-body">
          <div class="stat-value">{{ status.pending ?? 0 }}</div>
          <div class="stat-label">Pending</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon" style="color:var(--green)">✅</div>
        <div class="stat-body">
          <div class="stat-value" style="color:var(--green)">{{ status.processed ?? 0 }}</div>
          <div class="stat-label">Processed</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon" style="color:var(--red)">❌</div>
        <div class="stat-body">
          <div class="stat-value" style="color:var(--red)">{{ status.errored ?? 0 }}</div>
          <div class="stat-label">Errors</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon">🏢</div>
        <div class="stat-body">
          <div class="stat-value">{{ (status.tenantFolders ?? []).length }}</div>
          <div class="stat-label">Tenant Folders</div>
        </div>
      </div>
    </div>

      <div v-for="r in uploadResults" :key="r.file" class="upload-result-row">
        <span class="status-pill" :class="pillClass(r.status)">{{ r.status }}</span>
        <span style="font-weight:500; font-size:13px">{{ r.file }}</span>
        <span class="text-slate text-sm">{{ r.patient || '' }}</span>
        <span class="text-slate text-sm">{{ r.notes }}</span>
        <span v-if="r.saved" style="color:var(--green); font-size:12px; font-weight:600">{{ r.saved }} saved</span>
      </div>
    </div>

    <!-- Quarantine panel -->
    <div v-if="showQuarantine" class="card" style="margin-bottom:16px">
      <div class="card-header">
        <div>
          <div class="card-title">🔴 Quarantine</div>
          <div class="card-subtitle">Errored and duplicate HL7 files — review and action</div>
        </div>
      </div>
      <div v-if="!quarantineFiles.length" class="empty-state" style="padding:24px">
        <div class="empty-icon">✅</div>
        <div class="empty-title">No quarantined files</div>
      </div>
      <div v-else class="table-card">
        <!-- Bulk action bar -->
        <div v-if="selectedQuarantine.length" class="bulk-bar">
          <span class="bulk-count">{{ selectedQuarantine.length }} selected</span>
          <button class="btn btn-sm btn-danger" :disabled="bulkDeletingQuarantine" @click="deleteSelectedQuarantine">
            {{ bulkDeletingQuarantine ? 'Deleting…' : '🗑 Delete Selected' }}
          </button>
          <button class="btn btn-outline btn-sm" @click="selectedQuarantine = []">✕ Deselect</button>
        </div>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th style="width:36px; text-align:center">
                  <input type="checkbox" :checked="allQuarantineSelected" @change="toggleSelectAllQuarantine" class="row-check" />
                </th>
                <th>File</th><th>Reason</th><th>Size</th><th>Date</th><th style="text-align:center">Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="f in quarantineFiles" :key="f.path" :class="{ 'row-reviewing': reviewFile?.path === f.path }">
                <td style="text-align:center">
                  <input type="checkbox" :value="f.path" v-model="selectedQuarantine" class="row-check" />
                </td>
                <td class="text-sm" style="font-family:monospace; max-width:260px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap">{{ f.fileName }}</td>
                <td>
                  <span class="status-pill" :class="f.reason === 'duplicate' ? 'pill-warning' : 'pill-error'">
                    {{ f.reason === 'duplicate' ? 'Duplicate' : 'Error' }}
                  </span>
                </td>
                <td class="text-sm text-slate">{{ (f.size/1024).toFixed(1) }} KB</td>
                <td class="text-sm text-slate" style="white-space:nowrap">{{ new Date(f.modified).toLocaleString() }}</td>
                <td style="text-align:center">
                  <div class="flex gap-2" style="justify-content:center">
                    <button class="action-btn view" title="Review" @click="openReview(f)">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>
                    </button>
                    <button class="action-btn reprocess" :title="f.reason === 'duplicate' ? 'Acknowledge' : 'Reprocess'" :disabled="reprocessingPath===f.path" @click="reprocess(f)">
                      <svg v-if="f.reason === 'duplicate'" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                      <svg v-else width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 .49-3.12"/></svg>
                    </button>
                    <button class="action-btn delete" title="Delete" :disabled="deletingPath===f.path" @click="deleteQuarantine(f)">
                      <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/><line x1="10" y1="11" x2="10" y2="17"/><line x1="14" y1="11" x2="14" y2="17"/></svg>
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- Review Slide-over -->
    <teleport to="body">
      <div v-if="reviewFile" class="review-overlay" @click.self="closeReview">
        <div class="review-panel">
          <div class="review-header">
            <div>
              <div class="review-title">HL7 File Review</div>
              <div style="font-family:monospace; font-size:12px; color:var(--text-muted); margin-top:2px">{{ reviewFile.fileName }}</div>
            </div>
            <div class="flex gap-2" style="align-items:center">
              <span class="status-pill" :class="reviewFile.reason === 'duplicate' ? 'pill-warning' : 'pill-error'">{{ reviewFile.reason }}</span>
              <button class="btn btn-outline btn-sm" @click="closeReview">✕ Close</button>
            </div>
          </div>

          <div v-if="reviewLoading" style="display:flex; align-items:center; justify-content:center; padding:48px">
            <span class="text-slate">Loading file…</span>
          </div>

          <div v-else-if="reviewData" class="review-body">
            <!-- Error banner -->
            <div v-if="reviewFile.reason === 'error'" class="error-banner">
              <div class="error-banner-icon">⚠</div>
              <div style="flex:1">
                <div class="error-banner-title">Processing Error</div>
                <div class="error-banner-msg">{{ reviewData.quarantineReason || 'This file failed to process and was moved to quarantine. Review the parsed fields and raw content below, fix any mapping issues, then reprocess.' }}</div>
                <div v-if="reviewData.unmappedTestCodes?.length" class="mapping-issue">
                  <div class="mapping-issue-label">
                    Unmapped OBR-4 test code(s):
                    <span v-for="c in reviewData.unmappedTestCodes" :key="c" class="code-chip">{{ c }}</span>
                  </div>
                  <div class="mapping-issue-hint">
                    Add a mapping in
                    <router-link to="/settings" @click="closeReview">Settings → HL7 Code Mappings → Test Mappings (OBR-4)</router-link>
                  </div>
                </div>
                <div v-if="reviewData.unmappedAnalyteCodes?.length" class="mapping-issue">
                  <div class="mapping-issue-label">
                    Unmapped OBX-3 analyte code(s):
                    <span v-for="c in reviewData.unmappedAnalyteCodes" :key="c" class="code-chip">{{ c }}</span>
                  </div>
                  <div class="mapping-issue-hint">
                    Add mapping(s) in
                    <router-link to="/settings" @click="closeReview">Settings → HL7 Code Mappings → Analyte Mappings (OBX-3)</router-link>
                  </div>
                </div>
              </div>
            </div>
            <div v-else-if="reviewFile.reason === 'duplicate'" class="dupe-banner">
              <div class="error-banner-icon">⚡</div>
              <div>
                <div class="error-banner-title">Duplicate Message</div>
                <div class="error-banner-msg">MSH-10 message control ID already exists in the database. If this file was re-sent in error, click Acknowledge to mark it as reviewed.</div>
              </div>
            </div>
            <div class="review-section-title">Parsed Fields</div>
            <div class="review-fields">
              <div class="review-field"><span class="rf-label">Message Type</span><span class="rf-value">{{ reviewData.parsed.messageType || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Message ID</span><span class="rf-value mono">{{ reviewData.parsed.messageId || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Sending Facility</span><span class="rf-value">{{ reviewData.parsed.sendingFacility || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Patient Name</span><span class="rf-value" style="font-weight:600">{{ reviewData.parsed.patientName || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Patient ID</span><span class="rf-value mono">{{ reviewData.parsed.patientId || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Birthdate</span><span class="rf-value">{{ reviewData.parsed.birthdate || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Gender</span><span class="rf-value">{{ reviewData.parsed.gender || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Accession ID</span><span class="rf-value mono">{{ reviewData.parsed.accessionId || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Test Code</span><span class="rf-value mono">{{ reviewData.parsed.testCode || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Test Name</span><span class="rf-value">{{ reviewData.parsed.testName || '—' }}</span></div>
            </div>

            <template v-if="reviewData.parsed.observations?.length">
              <div class="review-section-title" style="margin-top:20px">Observations ({{ reviewData.parsed.observations.length }})</div>
              <div class="table-card" style="margin:0 0 16px">
                <div class="table-wrap">
                  <table>
                    <thead><tr><th>Code</th><th>Test</th><th>Value</th><th>Units</th><th>Ref Range</th><th>Flag</th><th>Status</th></tr></thead>
                    <tbody>
                      <tr v-for="(o, i) in reviewData.parsed.observations" :key="i">
                        <td class="text-sm mono">{{ o.testCode || '—' }}</td>
                        <td class="text-sm">{{ o.testName || '—' }}</td>
                        <td class="text-sm" style="font-weight:600">{{ o.resultValue || '—' }}</td>
                        <td class="text-sm text-slate">{{ o.resultUnit || '—' }}</td>
                        <td class="text-sm text-slate">{{ o.referenceRange || '—' }}</td>
                        <td class="text-sm">
                          <span v-if="o.abnormalFlag" class="status-pill pill-error" style="font-size:11px">{{ o.abnormalFlag }}</span>
                          <span v-else class="text-slate">—</span>
                        </td>
                        <td class="text-sm text-slate">{{ o.resultStatus || '—' }}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>
            </template>

            <div class="review-section-title">Raw HL7 Content</div>
            <pre class="hl7-raw">{{ reviewData.raw }}</pre>
          </div>

          <div class="review-footer">
            <span class="text-slate text-sm">{{ reviewFile.fileName }}</span>
            <div class="flex gap-2">
              <button class="btn btn-outline" @click="closeReview">Close</button>
              <button class="btn"
                :class="reviewFile.reason === 'duplicate' ? 'btn-outline' : 'btn-primary'"
                :disabled="reprocessingPath === reviewFile.path"
                @click="reprocessFromReview">
                <span v-if="reprocessingPath === reviewFile.path">Processing…</span>
                <span v-else-if="reviewFile.reason === 'duplicate'">✓ Acknowledge &amp; Close</span>
                <span v-else>↺ Reprocess</span>
              </button>
            </div>
          </div>
        </div>
      </div>
    </teleport>
    <!-- Log Row Detail Modal -->
    <teleport to="body">
      <div v-if="logReview" class="review-overlay" @click.self="logReview=null">
        <div class="review-panel">
          <div class="review-header">
            <div>
              <div class="review-title">Log Entry Detail</div>
              <div style="font-family:monospace; font-size:12px; color:var(--text-muted); margin-top:2px">{{ logReview.file || '—' }}</div>
            </div>
            <div class="flex gap-2" style="align-items:center">
              <span class="status-pill" :class="pillClass(logReview.status)">{{ logReview.status }}</span>
              <button class="btn btn-outline btn-sm" @click="logReview=null">✕ Close</button>
            </div>
          </div>

          <div v-if="logReviewLoading" style="display:flex; align-items:center; justify-content:center; padding:48px">
            <span class="text-slate">Loading file…</span>
          </div>

          <div v-else class="review-body">
            <!-- Error banner -->
            <div v-if="logReview.status === 'error'" class="error-banner">
              <div class="error-banner-icon">⚠</div>
              <div style="flex:1">
                <div class="error-banner-title">Processing Error</div>
                <div class="error-banner-msg">
                  {{ logReviewData?.quarantineReason || logReview.notes || 'An error occurred while processing this HL7 message.' }}
                </div>
                <div v-if="logReviewData?.unmappedTestCodes?.length" class="mapping-issue">
                  <div class="mapping-issue-label">
                    Unmapped OBR-4 test code(s):
                    <span v-for="c in logReviewData.unmappedTestCodes" :key="c" class="code-chip">{{ c }}</span>
                  </div>
                  <div class="mapping-issue-hint">
                    Add a mapping in
                    <router-link to="/settings" @click="logReview=null">Settings → HL7 Code Mappings → Test Mappings (OBR-4)</router-link>
                  </div>
                </div>
                <div v-if="logReviewData?.unmappedAnalyteCodes?.length" class="mapping-issue">
                  <div class="mapping-issue-label">
                    Unmapped OBX-3 analyte code(s):
                    <span v-for="c in logReviewData.unmappedAnalyteCodes" :key="c" class="code-chip">{{ c }}</span>
                  </div>
                  <div class="mapping-issue-hint">
                    Add mapping(s) in
                    <router-link to="/settings" @click="logReview=null">Settings → HL7 Code Mappings → Analyte Mappings (OBX-3)</router-link>
                  </div>
                </div>
                <div v-if="!logReviewData && logReview.notes?.includes('unmapped')" class="mapping-issue">
                  <div class="mapping-issue-hint" style="margin-top:6px">
                    Go to <router-link to="/settings" @click="logReview=null">Settings → HL7 Code Mappings</router-link> to add the missing mappings, then reprocess from the 🔴 Quarantine panel.
                  </div>
                </div>
              </div>
            </div>
            <div v-else-if="logReview.status === 'duplicate'" class="dupe-banner">
              <div class="error-banner-icon">⚡</div>
              <div>
                <div class="error-banner-title">Duplicate Message</div>
                <div class="error-banner-msg">MSH-10 message control ID already exists. This message was not re-processed.</div>
              </div>
            </div>
            <div class="review-section-title">Log Summary</div>
            <div class="review-fields">
              <div class="review-field"><span class="rf-label">Timestamp</span><span class="rf-value mono">{{ logReview.timestamp || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Status</span><span class="rf-value">{{ logReview.status || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Message Type</span><span class="rf-value mono">{{ logReview.msgType || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Patient</span><span class="rf-value" style="font-weight:600">{{ cleanPatient(logReview.patient) || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Accession</span><span class="rf-value mono">{{ cleanAcc(logReview.accession) || '—' }}</span></div>
              <div class="review-field"><span class="rf-label">Results Saved</span><span class="rf-value">{{ savedCount(logReview.saved) }}</span></div>
              <div class="review-field"><span class="rf-label">File</span><span class="rf-value mono">{{ logReview.file || '—' }}</span></div>
            </div>

            <template v-if="logReview.notes">
              <div class="review-section-title" style="margin-top:16px">Notes / Error</div>
              <pre class="hl7-raw" style="white-space:pre-wrap; word-break:break-word; max-height:120px">{{ logReview.notes }}</pre>
            </template>

            <template v-if="logReviewData">
              <div class="review-section-title" style="margin-top:20px">Parsed HL7 Fields</div>
              <div class="review-fields">
                <div class="review-field"><span class="rf-label">Message Type</span><span class="rf-value mono">{{ logReviewData.parsed.messageType || '—' }}</span></div>
                <div class="review-field"><span class="rf-label">Message ID</span><span class="rf-value mono">{{ logReviewData.parsed.messageId || '—' }}</span></div>
                <div class="review-field"><span class="rf-label">Sending Facility</span><span class="rf-value">{{ logReviewData.parsed.sendingFacility || '—' }}</span></div>
                <div class="review-field"><span class="rf-label">Patient Name</span><span class="rf-value" style="font-weight:600">{{ logReviewData.parsed.patientName || '—' }}</span></div>
                <div class="review-field"><span class="rf-label">Patient ID</span><span class="rf-value mono">{{ logReviewData.parsed.patientId || '—' }}</span></div>
                <div class="review-field"><span class="rf-label">Birthdate</span><span class="rf-value">{{ logReviewData.parsed.birthdate || '—' }}</span></div>
                <div class="review-field"><span class="rf-label">Gender</span><span class="rf-value">{{ logReviewData.parsed.gender || '—' }}</span></div>
                <div class="review-field"><span class="rf-label">Accession ID</span><span class="rf-value mono">{{ logReviewData.parsed.accessionId || '—' }}</span></div>
                <div class="review-field"><span class="rf-label">Test Code</span><span class="rf-value mono">{{ logReviewData.parsed.testCode || '—' }}</span></div>
                <div class="review-field"><span class="rf-label">Test Name</span><span class="rf-value">{{ logReviewData.parsed.testName || '—' }}</span></div>
              </div>

              <template v-if="logReviewData.parsed.observations?.length">
                <div class="review-section-title" style="margin-top:20px">Observations ({{ logReviewData.parsed.observations.length }})</div>
                <div class="table-card" style="margin:0 0 16px">
                  <div class="table-wrap">
                    <table>
                      <thead><tr><th>Code</th><th>Test</th><th>Value</th><th>Units</th><th>Ref Range</th><th>Flag</th><th>Status</th></tr></thead>
                      <tbody>
                        <tr v-for="(o, i) in logReviewData.parsed.observations" :key="i">
                          <td class="text-sm mono">{{ o.testCode || '—' }}</td>
                          <td class="text-sm">{{ o.testName || '—' }}</td>
                          <td class="text-sm" style="font-weight:600">{{ o.value || '—' }}</td>
                          <td class="text-sm text-slate">{{ o.units || '—' }}</td>
                          <td class="text-sm text-slate">{{ o.referenceRange || '—' }}</td>
                          <td class="text-sm">
                            <span v-if="o.abnormalFlag && o.abnormalFlag !== 'N'" class="status-pill pill-error" style="font-size:11px">{{ o.abnormalFlag }}</span>
                            <span v-else class="text-slate">—</span>
                          </td>
                          <td class="text-sm text-slate">{{ o.resultStatus || '—' }}</td>
                        </tr>
                      </tbody>
                    </table>
                  </div>
                </div>
              </template>

              <div class="review-section-title">Raw HL7 Content</div>
              <pre class="hl7-raw">{{ logReviewData.raw }}</pre>
            </template>

            <template v-else>
              <div style="margin-top:16px; padding:12px 16px; background:var(--bg-page); border:1px solid var(--border); border-radius:8px; display:flex; align-items:flex-start; gap:10px">
                <span style="font-size:16px; flex-shrink:0">📁</span>
                <div>
                  <div class="text-slate text-sm" style="font-weight:600">Archived file not found — log summary only.</div>
                  <div v-if="logReview.status === 'error' || logReview.status === 'duplicate'" class="text-slate text-sm" style="margin-top:4px">
                    Open the 🔴 Quarantine panel to view and reprocess the original file.
                  </div>
                </div>
              </div>
            </template>
          </div>

          <div class="review-footer">
            <span class="text-slate text-sm">{{ logReview.timestamp }}</span>
            <button class="btn btn-outline" @click="logReview=null">Close</button>
          </div>
        </div>
      </div>
    </teleport>

    <!-- Log table -->
    <div class="card">
      <div class="card-header">
        <div>
          <div class="card-title">Processing Log</div>
          <div class="card-subtitle">Most recent {{ logEntries.length }} entries</div>
        </div>
        <div class="flex gap-2">
          <input v-model="logSearch" class="form-input" style="width:220px; margin:0" placeholder="Search log..." />
          <select v-model="logStatusFilter" class="form-input" style="width:150px; margin:0">
            <option value="">All Status</option>
            <option value="order_saved">Order Saved</option>
            <option value="processed">Processed</option>
            <option value="duplicate">Duplicate</option>
            <option value="error">Error</option>
          </select>
        </div>
      </div>

      <template v-if="loadingLog">
        <div class="loading">Loading log...</div>
      </template>
      <template v-else-if="filteredLog.length === 0">
        <div class="empty-state">
          <div class="empty-icon">📭</div>
          <div class="empty-title">No log entries yet</div>
          <div class="text-slate text-sm">Drop HL7 files into the inbox folder or upload above</div>
        </div>
      </template>
      <template v-else>
        <!-- Bulk action toolbar -->
        <div v-if="selected.length" class="bulk-bar">
          <span class="bulk-count">{{ selected.length }} selected</span>
          <button class="btn btn-sm" style="background:#dc2626; color:white; border:none" @click="deleteSelected">🗑 Delete Selected</button>
          <button class="btn btn-outline btn-sm" @click="selected = []">✕ Deselect</button>
        </div>
        <div class="table-card">
          <div class="table-wrap">
            <table>
            <thead>
              <tr>
                <th style="width:36px; text-align:center"><input type="checkbox" :checked="allPageSelected" @change="toggleSelectAll" class="row-check" /></th>
                <th>Time</th>
                <th>Status</th>
                <th>Msg Type</th>
                <th>Patient</th>
                <th>Accession</th>
                <th>Saved</th>
                <th>File</th>
                <th>Notes</th>
                <th style="text-align:center">Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="e in pagedLog" :key="e._idx" class="log-row-clickable" @click.self="openLogReview(e)" style="cursor:pointer">
                <td style="text-align:center"><input type="checkbox" :value="e._idx" v-model="selected" class="row-check" /></td>
                <td class="text-sm" style="white-space:nowrap; font-family:monospace">{{ e.timestamp }}</td>
                <td>
                  <span class="status-pill" :class="pillClass(e.status)">{{ e.status }}</span>
                </td>
                <td class="text-sm" style="font-family:monospace">{{ e.msgType || '—' }}</td>
                <td class="text-sm" style="font-weight:500">{{ cleanPatient(e.patient) || '—' }}</td>
                <td class="text-sm" style="font-family:monospace">{{ cleanAcc(e.accession) || '—' }}</td>
                <td class="text-sm" style="text-align:center">
                  <span v-if="savedCount(e.saved) > 0" style="color:var(--green); font-weight:600">
                    {{ savedCount(e.saved) }}
                  </span>
                  <span v-else class="text-slate">—</span>
                </td>
                <td class="text-sm text-slate" style="max-width:180px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap">{{ e.file }}</td>
                <td class="text-sm text-slate" style="max-width:240px">{{ e.notes }}</td>
                <td style="text-align:center">
                  <div class="flex gap-2" style="justify-content:center">
                    <button class="action-btn view" title="Review" @click.stop="openLogReview(e)">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>
                    </button>
                    <button class="action-btn delete" title="Delete" @click.stop="deleteLogEntry(e)">
                      <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/><line x1="10" y1="11" x2="10" y2="17"/><line x1="14" y1="11" x2="14" y2="17"/></svg>
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
          </div>
          <div class="table-footer">
            <span>Showing {{ pagedLog.length }} of {{ filteredLog.length }} record{{ filteredLog.length !== 1 ? 's' : '' }}</span>
            <div class="pagination-wrap">
              <button class="page-btn" :disabled="page === 1" @click="page--">‹</button>
              <span class="page-info">{{ page }} / {{ totalPages }}</span>
              <button class="page-btn" :disabled="page >= totalPages" @click="page++">›</button>
              <select v-model="pageSize" class="page-size-select">
                <option :value="10">10</option>
                <option :value="25">25</option>
                <option :value="50">50</option>
                <option :value="100">100</option>
              </select>
            </div>
          </div>
        </div>
      </template>

      <div style="padding:12px 20px; border-top:1px solid var(--border); display:flex; justify-content:space-between; align-items:center; gap:12px">
        <div class="flex gap-2" style="align-items:center">
          <span class="text-slate text-sm">{{ logEntries.length }} total entries</span>
          <button v-if="auth.isAdmin && logEntries.length" class="btn btn-outline btn-sm" style="color:var(--red); border-color:var(--red); font-size:12px" @click="clearLog">🗑 Clear Log</button>
        </div>
        <div class="text-slate text-sm">Inbox: <span style="font-family:monospace">{{ status.inboxPath }}</span></div>
      </div>
    </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import api from '../services/api'
import { useAuthStore } from '../store/auth'
import { useDialog } from '../composables/useDialog'
const dialog = useDialog()
const auth = useAuthStore()

const loading    = ref(false)
const loadingLog = ref(false)
const status     = ref({})
const logEntries = ref([])
const logSearch  = ref('')
const logStatusFilter = ref('')
const page     = ref(1)
const pageSize = ref(25)
const selected = ref([])
let autoRefresh  = null
const showQuarantine   = ref(false)
const quarantineFiles  = ref([])
const reprocessingPath = ref(null)
const deletingPath     = ref(null)
const selectedQuarantine     = ref([])
const bulkDeletingQuarantine = ref(false)
const reviewFile             = ref(null)
const reviewData       = ref(null)
const reviewLoading    = ref(false)
const logReview        = ref(null)
const logReviewData    = ref(null)
const logReviewLoading = ref(false)

const filteredLog = computed(() => {
  let entries = logEntries.value.map((e, i) => ({ ...e, _idx: i }))
  if (logStatusFilter.value)
    entries = entries.filter(e => e.status?.trim() === logStatusFilter.value)
  if (logSearch.value) {
    const s = logSearch.value.toLowerCase()
    entries = entries.filter(e =>
      e.patient?.toLowerCase().includes(s) ||
      e.file?.toLowerCase().includes(s) ||
      e.accession?.toLowerCase().includes(s) ||
      e.notes?.toLowerCase().includes(s)
    )
  }
  return entries
})

const totalPages = computed(() => Math.max(1, Math.ceil(filteredLog.value.length / pageSize.value)))

const pagedLog = computed(() => {
  const start = (page.value - 1) * pageSize.value
  return filteredLog.value.slice(start, start + pageSize.value)
})

// Reset to page 1 when filter/search changes
function resetPage() { page.value = 1 }
watch([logSearch, logStatusFilter, pageSize], resetPage)

async function load() {
  loading.value = true
  try {
    const [statusRes, logRes] = await Promise.all([
      api.get('/hl7/inbox/status'),
      api.get('/hl7/log?lines=200')
    ])
    status.value     = statusRes.data
    logEntries.value = logRes.data.entries || []

    // Derive counts from log if folder counts are zero
    if (!status.value.processed && logEntries.value.length > 0) {
      status.value = {
        ...status.value,
        processed: logEntries.value.filter(e => e.status?.trim() === 'order_saved' || e.status?.trim() === 'processed').length,
        errored:   logEntries.value.filter(e => e.status?.trim() === 'error').length,
      }
    }
  } catch (e) {
    console.error('HL7 load error', e)
  } finally {
    loading.value = false
  }
}


function pillClass(status) {
  const s = (status || '').trim()
  if (s === 'processed' || s === 'order_saved') return 'pill-ready'
  if (s === 'error') return 'pill-error'
  if (s === 'duplicate') return 'pill-dupe'
  if (s === 'order_received' || s === 'order_only') return 'pill-order'
  return 'pill-neutral'
}

function cleanPatient(p) {
  return (p || '').replace(/^Patient:\s*/i, '').trim()
}

function cleanAcc(a) {
  return (a || '').replace(/^Acc:\s*/i, '').trim()
}

function savedCount(s) {
  const match = (s || '').match(/\d+/)
  return match ? parseInt(match[0]) : 0
}

const allPageSelected = computed(() =>
  pagedLog.value.length > 0 && pagedLog.value.every(e => selected.value.includes(e._idx))
)

function toggleSelectAll(evt) {
  if (evt.target.checked)
    selected.value = [...new Set([...selected.value, ...pagedLog.value.map(e => e._idx)])]
  else
    selected.value = selected.value.filter(i => !pagedLog.value.map(e => e._idx).includes(i))
}

async function deleteSelected() {
  if (!selected.value.length) return
  if (!await dialog.confirm(`Delete ${selected.value.length} selected entries?`, 'Delete Entries')) return
  const sorted = [...selected.value].sort((a, b) => b - a)
  try {
    for (const idx of sorted) {
      await api.delete('/hl7/log/' + idx)
      logEntries.value.splice(idx, 1)
    }
    selected.value = []
  } catch (e) { await dialog.alert('Failed: ' + (e.response?.data?.message || e.message), 'Error') }
}

async function deleteLogEntry(entry) {
  if (!await dialog.confirm('Delete this log entry?', 'Delete Entry')) return
  try {
    await api.delete('/hl7/log/' + entry._idx)
    logEntries.value.splice(entry._idx, 1)
    selected.value = selected.value.filter(i => i !== entry._idx)
  } catch (e) { await dialog.alert('Failed: ' + (e.response?.data?.message || e.message), 'Error') }
}

async function clearLog() {
  if (!await dialog.confirm('Clear all log entries? This cannot be undone.', 'Clear Log')) return
  try {
    await api.delete('/hl7/log')
    logEntries.value = []
  } catch (e) { await dialog.alert('Failed: ' + (e.response?.data?.message || e.message), 'Error') }
}

async function loadQuarantine() {
  try {
    const { data } = await api.get('/hl7/quarantine')
    quarantineFiles.value = data.files || []
  } catch {}
}

async function reprocess(file) {
  reprocessingPath.value = file.path
  try {
    const { data } = await api.post('/hl7/quarantine/reprocess', { path: file.path })
    if (data.status !== 'error') {
      quarantineFiles.value = quarantineFiles.value.filter(f => f.path !== file.path)
      await load()
    } else {
      await dialog.alert('Reprocess failed: ' + (data.notes || 'Unknown error'), 'Reprocess Failed')
    }
  } catch (e) {
    await dialog.alert('Failed: ' + (e.response?.data?.message || e.message), 'Error')
  } finally {
    reprocessingPath.value = null
  }
}

async function openReview(file) {
  reviewFile.value  = file
  reviewData.value  = null
  reviewLoading.value = true
  try {
    const { data } = await api.get('/hl7/quarantine/read', { params: { path: file.path } })
    reviewData.value = data
  } catch (e) {
    await dialog.alert('Could not load file: ' + (e.response?.data?.message || e.message), 'Error')
    reviewFile.value = null
  } finally {
    reviewLoading.value = false
  }
}

function closeReview() {
  reviewFile.value = null
  reviewData.value = null
}

async function openLogReview(entry) {
  logReview.value   = entry
  logReviewData.value   = null
  logReviewLoading.value = true
  try {
    const { data } = await api.get('/hl7/log/read', { params: { fileName: entry.file } })
    logReviewData.value = data
  } catch (e) {
    // File not found is OK — show log fields only
    logReviewData.value = null
  } finally {
    logReviewLoading.value = false
  }
}

async function reprocessFromReview() {
  if (!reviewFile.value) return
  const file = reviewFile.value
  await reprocess(file)
  closeReview()
}


async function deleteQuarantine(file) {
  if (!await dialog.confirm(`Permanently delete "${file.fileName}"? This cannot be undone.`, 'Delete File')) return
  deletingPath.value = file.path
  try {
    await api.delete('/hl7/quarantine', { params: { path: file.path } })
    quarantineFiles.value = quarantineFiles.value.filter(f => f.path !== file.path)
    selectedQuarantine.value = selectedQuarantine.value.filter(p => p !== file.path)
  } catch (e) {
    await dialog.alert('Delete failed: ' + (e.response?.data?.message || e.message), 'Error')
  } finally {
    deletingPath.value = null
  }
}

const allQuarantineSelected = computed(() =>
  quarantineFiles.value.length > 0 &&
  quarantineFiles.value.every(f => selectedQuarantine.value.includes(f.path))
)

function toggleSelectAllQuarantine(evt) {
  if (evt.target.checked)
    selectedQuarantine.value = quarantineFiles.value.map(f => f.path)
  else
    selectedQuarantine.value = []
}

async function deleteSelectedQuarantine() {
  if (!selectedQuarantine.value.length) return
  if (!await dialog.confirm(`Permanently delete ${selectedQuarantine.value.length} quarantined file(s)? This cannot be undone.`, 'Delete Files')) return
  bulkDeletingQuarantine.value = true
  try {
    for (const path of selectedQuarantine.value) {
      try { await api.delete('/hl7/quarantine', { params: { path } }) } catch { /* continue on individual failure */ }
    }
    quarantineFiles.value = quarantineFiles.value.filter(f => !selectedQuarantine.value.includes(f.path))
    selectedQuarantine.value = []
  } finally {
    bulkDeletingQuarantine.value = false
  }
}

onMounted(() => {
  load()
  loadQuarantine()
  autoRefresh = setInterval(() => { load(); loadQuarantine() }, 15000)
})
onUnmounted(() => clearInterval(autoRefresh))
</script>

<style scoped>
.stat-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 14px; }
.stat-card { background: white; border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 18px 20px; display: flex; align-items: center; gap: 14px; box-shadow: var(--shadow-sm); }
.stat-icon { font-size: 24px; }
.stat-value { font-family: 'Plus Jakarta Sans', sans-serif; font-size: 26px; font-weight: 800; color: var(--navy); line-height: 1; }
.stat-label { font-size: 12px; color: var(--slate); margin-top: 3px; }

.status-pill { display: inline-block; padding: 3px 10px; border-radius: 20px; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.4px; white-space: nowrap; }
.pill-ready   { background: #dcfce7; color: #166534; }
.pill-error    { background: #fee2e2; color: #991b1b; }
.pill-warning  { background: #fef9c3; color: #854d0e; }
.pill-dupe    { background: #f1f5f9; color: var(--slate); }
.pill-order   { background: #dbeafe; color: #1e40af; }
.pill-neutral { background: #f1f5f9; color: var(--slate); }

.upload-result-row { display: flex; align-items: center; gap: 12px; padding: 8px 0; border-bottom: 1px solid var(--border-light); flex-wrap: wrap; }
.upload-result-row:last-child { border-bottom: none; }

.pagination-wrap { display:flex; align-items:center; gap:6px; }
.page-btn { width:28px; height:28px; border:1px solid var(--border); border-radius:6px; background:white; cursor:pointer; font-size:14px; display:flex; align-items:center; justify-content:center; }
.page-btn:disabled { opacity:0.4; cursor:not-allowed; }
.page-btn:not(:disabled):hover { background:var(--teal); color:white; border-color:var(--teal); }
.page-info { font-size:12px; color:var(--slate); min-width:48px; text-align:center; }
.page-size-select { height:28px; padding:0 6px; border:1px solid var(--border); border-radius:6px; font-size:12px; color:var(--slate); }
.bulk-bar { display:flex; align-items:center; gap:10px; padding:10px 20px; background:#fef2f2; border-bottom:1px solid #fecaca; }
.bulk-count { font-size:13px; font-weight:600; color:#dc2626; }
.row-check { width:15px; height:15px; cursor:pointer; accent-color: var(--teal); }

/* ── Quarantine row highlight ─────────────────────────────── */
.row-reviewing { background: var(--primary-light) !important; }

/* ── Review slide-over overlay ───────────────────────────── */
.review-overlay {
  position: fixed; inset: 0; z-index: 1000;
  background: rgba(0,0,0,.45);
  display: flex; justify-content: flex-end;
}
.review-panel {
  width: min(760px, 100vw);
  height: 100vh;
  background: #ffffff;
  display: flex; flex-direction: column;
  box-shadow: -4px 0 24px rgba(0,0,0,.18);
  animation: slideIn .2s ease;
}
@keyframes slideIn {
  from { transform: translateX(100%); }
  to   { transform: translateX(0); }
}
.review-header {
  display: flex; justify-content: space-between; align-items: flex-start;
  padding: 20px 24px 16px;
  border-bottom: 1px solid #e5e7eb;
  background: #ffffff;
  flex-shrink: 0;
}
.review-title { font-size: 16px; font-weight: 700; color: var(--text); }
.review-body {
  flex: 1; overflow-y: auto;
  padding: 20px 24px;
  background: #ffffff;
}
.review-footer {
  display: flex; justify-content: space-between; align-items: center;
  padding: 14px 24px;
  border-top: 1px solid var(--border);
  flex-shrink: 0;
  background: #ffffff;
}
.review-section-title {
  font-size: 11px; font-weight: 700; text-transform: uppercase;
  letter-spacing: .06em; color: var(--text-muted);
  margin-bottom: 10px;
}
.review-fields {
  display: grid; grid-template-columns: 1fr 1fr;
  gap: 8px 16px;
  background: #f8fafc;
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 14px 16px;
}
.review-field { display: flex; flex-direction: column; gap: 2px; }
.rf-label { font-size: 11px; color: var(--text-muted); font-weight: 600; text-transform: uppercase; letter-spacing: .04em; }
.rf-value { font-size: 13px; color: var(--text); }
.rf-value.mono { font-family: monospace; }
.hl7-raw {
  background: #0f172a; color: #e2e8f0;
  font-family: monospace; font-size: 12px; line-height: 1.6;
  padding: 16px; border-radius: 8px;
  overflow-x: auto; white-space: pre;
  max-height: 320px; overflow-y: auto;
  margin: 0;
}
.log-row-clickable:hover td { background: #f0f6ff; }
.btn-danger { background: #dc2626; color: white; border: none; }
.btn-danger:hover:not(:disabled) { background: #b91c1c; }
.btn-danger:disabled { opacity: 0.5; cursor: not-allowed; }

/* Error / dupe banners in review panel */
.error-banner  { display:flex; gap:12px; align-items:flex-start; background:#fef2f2; border:1.5px solid #fca5a5; border-radius:8px; padding:12px 16px; margin-bottom:18px; }
.dupe-banner   { display:flex; gap:12px; align-items:flex-start; background:#fefce8; border:1.5px solid #fde047; border-radius:8px; padding:12px 16px; margin-bottom:18px; }
.error-banner-icon  { font-size:20px; line-height:1; flex-shrink:0; }
.error-banner-title { font-size:13px; font-weight:700; color:#991b1b; margin-bottom:3px; }
.dupe-banner .error-banner-title { color:#854d0e; }
.error-banner-msg   { font-size:12px; color:#7f1d1d; line-height:1.5; }
.dupe-banner .error-banner-msg  { color:#713f12; }

/* Mapping issue blocks inside error banner */
.mapping-issue { margin-top:10px; }
.mapping-issue-label { font-size:12px; font-weight:600; color:#991b1b; display:flex; align-items:center; gap:6px; flex-wrap:wrap; }
.mapping-issue-hint  { font-size:11px; color:#7f1d1d; margin-top:3px; }
.mapping-issue-hint a { color:#0d7377; font-weight:600; text-decoration:underline; }
.code-chip { display:inline-block; background:#fee2e2; border:1px solid #fca5a5; color:#991b1b; font-family:monospace; font-size:11px; font-weight:700; padding:1px 7px; border-radius:4px; }
</style>