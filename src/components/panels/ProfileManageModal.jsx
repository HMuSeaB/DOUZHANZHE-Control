import { useState, useRef, useEffect } from 'react';
import { renameProfile, deleteProfile, copyProfile, resetProfile } from '../../services/uxtuAdapter';

const THERMAL_LABELS = { silent: '安静', office: '均衡', gaming: '斗战', beast: '野兽' };

export default function ProfileManageModal({
  open, onClose, profiles, setProfiles, currentProfile, setCurrentProfile,
  switchProfile, afterProfileDeleted,
}) {
  const [editingId, setEditingId] = useState(null);
  const [editName, setEditName] = useState('');
  const [confirmDelete, setConfirmDelete] = useState(null);
  const inputRef = useRef(null);

  useEffect(() => { if (editingId && inputRef.current) inputRef.current.focus(); }, [editingId]);

  if (!open) return null;

  async function handleRename(id) {
    if (!editName.trim()) return;
    try {
      await renameProfile(id, editName.trim());
      setProfiles(prev => prev.map(p => p.id === id ? { ...p, name: editName.trim() } : p));
      if (currentProfile?.id === id) setCurrentProfile(prev => prev ? { ...prev, name: editName.trim() } : prev);
      setEditingId(null);
      setEditName('');
    } catch (e) { console.error('rename failed:', e); }
  }

  async function handleDelete(id) {
    try {
      await deleteProfile(id);
      afterProfileDeleted(id);
      setConfirmDelete(null);
    } catch (e) { console.error('delete failed:', e); }
  }

  async function handleCopy(id) {
    try {
      const entry = await copyProfile(id);
      setProfiles(prev => [...prev, entry]);
    } catch (e) { console.error('copy failed:', e); }
  }

  async function handleReset(id) {
    try {
      await resetProfile(id);
    } catch (e) { console.error('reset failed:', e); }
  }

  return (
    <div className="modal show" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-content profile-manage-modal">
        <div className="modal-head">
          <h2>{'\u7ba1\u7406\u914d\u7f6e'}</h2>
          <button className="btn ghost" onClick={onClose}>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" width="18" height="18"><path d="M18 6 6 18M6 6l12 12"/></svg>
          </button>
        </div>
        <div className="profile-list">
          {profiles.map(p => (
            <div key={p.id} className={'profile-row' + (currentProfile?.id === p.id ? ' current' : '')}>
              <div className="profile-info">
                <span className="profile-badge">{p.name.charAt(0).toUpperCase()}</span>
                <div>
                  {editingId === p.id ? (
                    <div className="rename-inline">
                      <input ref={inputRef} type="text" className="config-name-input" value={editName}
                        onChange={e => setEditName(e.target.value)}
                        onKeyDown={e => { if (e.key === 'Enter') handleRename(p.id); if (e.key === 'Escape') setEditingId(null); }} />
                      <button className="btn primary" style={{ padding: '4px 10px', fontSize: 12 }} onClick={() => handleRename(p.id)}>{'\u786e\u8ba4'}</button>
                    </div>
                  ) : (
                    <>
                      <b>{p.name}</b>
                      <small>{p.builtIn ? '\u5185\u7f6e' : '\u7528\u6237'}{' \u00b7 \u6563\u70ed\uff1a'}{THERMAL_LABELS[p.thermalMode] || p.thermalMode}</small>
                    </>
                  )}
                </div>
              </div>
              <div className="profile-actions">
                <button className="btn" style={{ padding: '4px 10px', fontSize: 12 }} onClick={() => { setEditingId(p.id); setEditName(p.name); }}
                  disabled={p.builtIn}>{'\u91cd\u547d\u540d'}</button>
                <button className="btn" style={{ padding: '4px 10px', fontSize: 12 }} onClick={() => handleCopy(p.id)}>
                  {'\u590d\u5236'}
                </button>
                {!p.builtIn && (
                  <>
                    <button className="btn" style={{ padding: '4px 10px', fontSize: 12 }} onClick={() => handleReset(p.id)}>
                      {'\u91cd\u7f6e\u51fa\u5382'}
                    </button>
                    {confirmDelete === p.id ? (
                      <>
                        <span style={{ color: 'var(--danger)', fontSize: 12 }}>{'\u786e\u8ba4\u5220\u9664\uff1f'}</span>
                        <button className="btn" style={{ padding: '4px 10px', fontSize: 12, color: 'var(--danger)' }} onClick={() => handleDelete(p.id)}>{'\u786e\u8ba4'}</button>
                        <button className="btn" style={{ padding: '4px 10px', fontSize: 12 }} onClick={() => setConfirmDelete(null)}>{'\u53d6\u6d88'}</button>
                      </>
                    ) : (
                      <button className="btn" style={{ padding: '4px 10px', fontSize: 12, color: 'var(--danger)' }}
                        onClick={() => setConfirmDelete(p.id)}>{'\u5220\u9664'}</button>
                    )}
                  </>
                )}
                {p.builtIn && (
                  <button className="btn" style={{ padding: '4px 10px', fontSize: 12 }} onClick={() => handleReset(p.id)}>
                    {'\u91cd\u7f6e\u51fa\u5382'}
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
