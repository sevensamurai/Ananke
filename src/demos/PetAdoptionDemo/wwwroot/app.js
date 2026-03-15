const chat = document.getElementById('chat');
const form = document.getElementById('form');
const input = document.getElementById('input');
const sendBtn = document.getElementById('send-btn');
const micBtn = document.getElementById('mic-btn');
const recordingStatus = document.getElementById('recording-status');
const photoBtn = document.getElementById('photo-btn');
const photoInput = document.getElementById('photo-input');
const photoPreview = document.getElementById('photo-preview');
const photoPreviewImg = document.getElementById('photo-preview-img');
const photoRemove = document.getElementById('photo-remove');

const history = [];
let sessionId = crypto.randomUUID();
let busy = false;
let mediaRecorder = null;
let audioChunks = [];
let pendingPhoto = null; // { base64, mimeType }

marked.setOptions({ breaks: true, gfm: true });

// ─── Form submission (text) ──────────────────────────────────

form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const text = input.value.trim();
    if (!text && !pendingPhoto) return;

    if (busy) {
        // Typing while agent is responding → interrupt
        await doInterrupt(text);
        input.value = '';
        return;
    }

    input.value = '';

    if (pendingPhoto) {
        const photo = pendingPhoto;
        const displayText = text || '📷 [Photo attached]';
        clearPendingPhoto();
        await sendMessage({
            message: text || null,
            imageBase64: photo.base64,
            imageMimeType: photo.mimeType
        }, displayText, photo);
    } else {
        await sendMessage({ message: text }, text);
    }
});

// ─── Microphone ──────────────────────────────────────────────

micBtn.addEventListener('click', async () => {
    if (mediaRecorder && mediaRecorder.state === 'recording') {
        mediaRecorder.stop();
        return;
    }

    try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        audioChunks = [];

        // Prefer wav-compatible format, fall back to webm
        const mimeType = MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
            ? 'audio/webm;codecs=opus'
            : 'audio/webm';

        mediaRecorder = new MediaRecorder(stream, { mimeType });

        mediaRecorder.ondataavailable = (e) => {
            if (e.data.size > 0) audioChunks.push(e.data);
        };

        mediaRecorder.onstop = async () => {
            stream.getTracks().forEach(t => t.stop());
            micBtn.classList.remove('recording');
            recordingStatus.hidden = true;

            const blob = new Blob(audioChunks, { type: mimeType });
            const base64 = await blobToBase64(blob);
            const displayText = '🎤 [Voice message]';

            await sendMessage({
                audioBase64: base64,
                audioMimeType: mimeType
            }, displayText);
        };

        mediaRecorder.start();
        micBtn.classList.add('recording');
        recordingStatus.hidden = false;
    } catch (err) {
        console.error('Microphone access denied:', err);
        alert('Microphone access is required for voice input.');
    }
});

// ─── Photo attachment ────────────────────────────────────────

photoBtn.addEventListener('click', () => photoInput.click());

photoInput.addEventListener('change', async () => {
    const file = photoInput.files[0];
    if (!file) return;

    const base64 = await fileToBase64(file);
    pendingPhoto = { base64, mimeType: file.type || 'image/jpeg' };

    photoPreviewImg.src = `data:${pendingPhoto.mimeType};base64,${base64}`;
    photoPreview.hidden = false;
    photoBtn.classList.add('has-photo');
    input.placeholder = 'Add a message about this photo…';
    input.focus();

    photoInput.value = '';
});

photoRemove.addEventListener('click', () => {
    clearPendingPhoto();
});

function clearPendingPhoto() {
    pendingPhoto = null;
    photoPreview.hidden = true;
    photoPreviewImg.src = '';
    photoBtn.classList.remove('has-photo');
    input.placeholder = busy
        ? 'Type to interrupt the agent…'
        : 'Ask about pets, adoption, care…';
}

async function fileToBase64(file) {
    return new Promise((resolve) => {
        const reader = new FileReader();
        reader.onloadend = () => resolve(reader.result.split(',')[1]);
        reader.readAsDataURL(file);
    });
}

// ─── Interrupt ───────────────────────────────────────────────

async function doInterrupt(text) {
    if (!busy) return;
    const message = text || 'The user interrupted the response.';

    // Show the interrupt text as a user message in the chat
    if (text) {
        appendMessage('user', text);
        history.push({ role: 'user', content: text });
    }

    try {
        const res = await fetch('/api/interrupt', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ sessionId, message })
        });
        if (!res.ok) {
            console.warn('Interrupt rejected:', res.status, await res.text().catch(() => ''));
        }
    } catch (err) {
        console.error('Interrupt failed:', err);
    }
}

// ─── Send message & stream response ─────────────────────────

async function sendMessage(payload, displayText, photo) {
    chat.querySelectorAll('.empty-state').forEach(el => el.remove());

    appendMessage('user', displayText, photo);
    history.push({ role: 'user', content: displayText });

    setBusy(true);

    let bubble = appendMessage('assistant', '');
    const tracker = createWorkflowTracker();
    chat.insertBefore(tracker, bubble);
    let accumulated = '';
    let pendingToolBadge = null;
    let wfPhase = 'idle';
    let wfRound = 0;
    let awaitingPayment = false;

    try {
        const body = {
            ...payload,
            sessionId,
            history: history.slice(0, -1)
        };

        const response = await fetch('/api/chat', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });

        if (!response.ok) {
            throw new Error(`Server error (${response.status})`);
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        let currentEvent = '';

        while (true) {
            const { done, value } = await reader.read();
            buffer += done
                ? decoder.decode()
                : decoder.decode(value, { stream: true });

            const lines = buffer.split(/\r\n|\r|\n/);
            buffer = lines.pop();

            for (const line of lines) {
                if (line.startsWith('event: ')) {
                    currentEvent = line.slice(7).trim();
                } else if (line.startsWith('data: ')) {
                    let data;
                    try { data = JSON.parse(line.slice(6)); }
                    catch { console.warn('SSE: skipping malformed data', line); continue; }

                    switch (currentEvent) {
                        case 'delta':
                            if (wfPhase !== 'agent') {
                                completeCurrentStep(tracker);
                                wfRound++;
                                addWorkflowStep(tracker, wfRound === 1 ? 'Thinking' : 'Synthesizing', '🧠');
                                wfPhase = 'agent';
                            }
                            accumulated += data.text;
                            bubble.innerHTML = marked.parse(accumulated);
                            bubble.classList.add('typing-indicator');
                            scrollToBottom();
                            break;

                        case 'tool_call':
                            if (wfPhase === 'idle') {
                                addWorkflowStep(tracker, 'Reasoning', '🧠');
                                completeCurrentStep(tracker);
                            } else {
                                completeCurrentStep(tracker);
                            }
                            addWorkflowStep(tracker, data.name, '🔧');
                            wfPhase = 'tool';
                            pendingToolBadge = appendToolBadge(data.name, bubble);
                            scrollToBottom();
                            break;

                        case 'tool_result':
                            completeCurrentStep(tracker);
                            if (pendingToolBadge) {
                                finishToolBadge(pendingToolBadge, data.name, data.result);
                                pendingToolBadge = null;
                            }
                            scrollToBottom();
                            break;

                        case 'interrupted': {
                            completeCurrentStep(tracker);
                            if (pendingToolBadge) {
                                finishToolBadge(pendingToolBadge, '…', 'interrupted');
                                pendingToolBadge = null;
                            }
                            const intArrow = document.createElement('span');
                            intArrow.className = 'wf-arrow';
                            intArrow.textContent = '→';
                            tracker.appendChild(intArrow);
                            const intStep = document.createElement('span');
                            intStep.className = 'wf-step interrupted';
                            intStep.textContent = '⚡ Interrupted';
                            tracker.appendChild(intStep);
                            appendInterruptBadge();
                            scrollToBottom();
                            break;
                        }

                        case 'resumed':
                            // Agent is re-generating — remove stale interrupt badge
                            removeInterruptBadges();
                            // Retire old bubble: keep partial text or remove if empty
                            if (!accumulated.trim()) {
                                bubble.remove();
                            } else {
                                bubble.classList.remove('typing-indicator');
                            }
                            // Fresh bubble after the interrupt message
                            accumulated = '';
                            bubble = appendMessage('assistant', '');
                            bubble.classList.add('typing-indicator');
                            wfPhase = 'idle';
                            break;

                        case 'payment_required':
                            completeCurrentStep(tracker);
                            addWorkflowStep(tracker, 'Awaiting payment', '💳');
                            showPaymentForm(data.petName, data.amount);
                            awaitingPayment = true;
                            scrollToBottom();
                            break;

                        case 'done':
                            removeInterruptBadges();
                            if (!awaitingPayment) {
                                removePaymentForms();
                                finalizeTracker(tracker);
                                history.push({ role: 'assistant', content: accumulated });
                            }
                            if (pendingToolBadge) {
                                finishToolBadge(pendingToolBadge, '…', '(completed)');
                                pendingToolBadge = null;
                            }
                            accumulated = data.text || accumulated;
                            bubble.innerHTML = marked.parse(accumulated);
                            bubble.classList.remove('typing-indicator');
                            break;

                        case 'error':
                            completeCurrentStep(tracker);
                            if (pendingToolBadge) {
                                finishToolBadge(pendingToolBadge, '…', `error: ${data.message}`);
                                pendingToolBadge = null;
                            }
                            bubble.textContent = `⚠️ ${data.message}`;
                            bubble.classList.remove('typing-indicator');
                            break;
                    }
                }
            }

            if (done) break;
        }

        // Safety net: stream ended — clean up anything left open
        removeInterruptBadges();
        if (pendingToolBadge) {
            finishToolBadge(pendingToolBadge, '…', '(completed)');
            pendingToolBadge = null;
        }
        if (!awaitingPayment && !tracker.querySelector('.wf-step.complete')) {
            finalizeTracker(tracker);
        }
        bubble.classList.remove('typing-indicator');
    } catch (err) {
        removeInterruptBadges();
        if (pendingToolBadge) {
            finishToolBadge(pendingToolBadge, '…', 'connection error');
            pendingToolBadge = null;
        }
        if (!tracker.querySelector('.wf-step.complete')) {
            finalizeTracker(tracker);
        }
        bubble.textContent = `Error: ${err.message}`;
        bubble.classList.remove('typing-indicator');
    }

    setBusy(false);
    input.focus();
}

// ─── UI helpers ──────────────────────────────────────────────

function appendMessage(role, content, photo) {
    const div = document.createElement('div');
    div.className = `msg ${role}`;
    if (role === 'user') {
        if (photo) {
            const img = document.createElement('img');
            img.src = `data:${photo.mimeType};base64,${photo.base64}`;
            img.className = 'msg-photo';
            img.alt = 'Attached photo';
            div.appendChild(img);
        }
        if (content && content !== '📷 [Photo attached]') {
            const textNode = document.createElement('span');
            textNode.textContent = content;
            div.appendChild(textNode);
        } else if (!photo) {
            div.textContent = content;
        }
    } else {
        div.innerHTML = content ? marked.parse(content) : '';
    }
    chat.appendChild(div);
    scrollToBottom();
    return div;
}

function appendToolBadge(name, beforeEl) {
    const badge = document.createElement('div');
    badge.className = 'tool-badge pending';
    badge.innerHTML = `🔍 ${escapeHtml(name)}…`;
    chat.insertBefore(badge, beforeEl);
    return badge;
}

function finishToolBadge(badge, name, result) {
    badge.classList.remove('pending');
    badge.classList.add('done');
    badge.innerHTML = `
        <details>
            <summary>✅ ${escapeHtml(name)}</summary>
            <pre>${escapeHtml(result)}</pre>
        </details>`;
}

function appendInterruptBadge() {
    const badge = document.createElement('div');
    badge.className = 'interrupt-badge';
    badge.textContent = '⚡ Interrupted — re-generating with your new input…';
    chat.appendChild(badge);
}

function removeInterruptBadges() {
    chat.querySelectorAll('.interrupt-badge').forEach(el => el.remove());
}

function scrollToBottom() {
    chat.scrollTop = chat.scrollHeight;
}

function setBusy(state) {
    busy = state;
    sendBtn.textContent = state ? '⚡ Send' : 'Send';
    input.placeholder = state
        ? 'Type to interrupt the agent…'
        : 'Ask about pets, adoption, care…';
}

function escapeHtml(s) {
    const d = document.createElement('div');
    d.textContent = s;
    return d.innerHTML;
}

// ─── Workflow tracker ────────────────────────────────────────

function createWorkflowTracker() {
    const tracker = document.createElement('div');
    tracker.className = 'workflow-tracker';
    const label = document.createElement('span');
    label.className = 'wf-label';
    label.textContent = '⚙️ Workflow';
    tracker.appendChild(label);
    return tracker;
}

function addWorkflowStep(tracker, label, icon) {
    if (tracker.querySelectorAll('.wf-step').length > 0) {
        const arrow = document.createElement('span');
        arrow.className = 'wf-arrow';
        arrow.textContent = '→';
        tracker.appendChild(arrow);
    }
    const step = document.createElement('span');
    step.className = 'wf-step active';
    step.textContent = `${icon} ${label}`;
    tracker.appendChild(step);
    scrollToBottom();
    return step;
}

function completeCurrentStep(tracker) {
    const active = tracker.querySelector('.wf-step.active');
    if (active) {
        active.classList.remove('active');
        active.classList.add('done');
    }
}

function finalizeTracker(tracker) {
    completeCurrentStep(tracker);
    const arrow = document.createElement('span');
    arrow.className = 'wf-arrow';
    arrow.textContent = '→';
    tracker.appendChild(arrow);
    const done = document.createElement('span');
    done.className = 'wf-step complete';
    done.textContent = '✅ Done';
    tracker.appendChild(done);
}

async function blobToBase64(blob) {
    return new Promise((resolve) => {
        const reader = new FileReader();
        reader.onloadend = () => {
            const base64 = reader.result.split(',')[1];
            resolve(base64);
        };
        reader.readAsDataURL(blob);
    });
}

// ─── Payment form (HITL) ─────────────────────────────────────

function showPaymentForm(petName, amount) {
    const feeText = amount != null ? `$${amount}` : 'the adoption fee';
    const petText = petName ? ` for **${petName}**` : '';
    const container = document.createElement('div');
    container.className = 'payment-form';
    container.innerHTML = `
        <p>💳 Enter your credit card number to pay ${feeText}${petText}.</p>
        <fieldset role="group">
            <input type="text" class="card-input" placeholder="Card number"
                   maxlength="19" autocomplete="cc-number" inputmode="numeric"
                   pattern="[0-9 ]*">
            <button type="button" class="pay-btn">Pay ${amount != null ? feeText : 'Now'}</button>
        </fieldset>
    `;
    chat.appendChild(container);

    const cardInput = container.querySelector('.card-input');
    const payBtn = container.querySelector('.pay-btn');

    // Format card number: digits only, max 16, spaces every 4
    cardInput.addEventListener('input', () => {
        const raw = cardInput.value.replace(/\D/g, '').slice(0, 16);
        cardInput.value = raw.replace(/(.{4})/g, '$1 ').trim();
    });

    payBtn.addEventListener('click', async () => {
        const cardNumber = cardInput.value.replace(/\s/g, '');
        if (!cardNumber || cardNumber.length < 13) {
            cardInput.setAttribute('aria-invalid', 'true');
            return;
        }

        payBtn.disabled = true;
        cardInput.disabled = true;
        container.classList.add('processing');

        await submitPayment(cardNumber);
    });

    cardInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') payBtn.click();
    });

    cardInput.focus();
}

function removePaymentForms() {
    chat.querySelectorAll('.payment-form').forEach(el => el.remove());
}

async function submitPayment(cardNumber) {
    setBusy(true);

    let bubble = appendMessage('assistant', '');
    const tracker = createWorkflowTracker();
    chat.insertBefore(tracker, bubble);
    addWorkflowStep(tracker, 'Processing payment', '💳');
    let accumulated = '';

    try {
        const response = await fetch('/api/payment', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ sessionId, cardNumber })
        });

        // Card number is now sent — clear the local variable immediately
        cardNumber = '';

        if (!response.ok) {
            throw new Error(`Payment error (${response.status})`);
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        let currentEvent = '';

        while (true) {
            const { done, value } = await reader.read();
            buffer += done
                ? decoder.decode()
                : decoder.decode(value, { stream: true });

            const lines = buffer.split(/\r\n|\r|\n/);
            buffer = lines.pop();

            for (const line of lines) {
                if (line.startsWith('event: ')) {
                    currentEvent = line.slice(7).trim();
                } else if (line.startsWith('data: ')) {
                    let data;
                    try { data = JSON.parse(line.slice(6)); }
                    catch { console.warn('SSE: skipping malformed data', line); continue; }

                    switch (currentEvent) {
                        case 'delta':
                            completeCurrentStep(tracker);
                            accumulated += data.text;
                            bubble.innerHTML = marked.parse(accumulated);
                            bubble.classList.add('typing-indicator');
                            scrollToBottom();
                            break;

                        case 'done':
                            removePaymentForms();
                            finalizeTracker(tracker);
                            accumulated = data.text || accumulated;
                            bubble.innerHTML = marked.parse(accumulated);
                            bubble.classList.remove('typing-indicator');
                            history.push({ role: 'assistant', content: accumulated });
                            // Adoption complete — start a fresh session for any follow-up questions
                            sessionId = crypto.randomUUID();
                            break;

                        case 'error':
                            completeCurrentStep(tracker);
                            bubble.textContent = `⚠️ ${data.message}`;
                            bubble.classList.remove('typing-indicator');
                            // Re-enable the payment form
                            const pf = chat.querySelector('.payment-form.processing');
                            if (pf) {
                                pf.classList.remove('processing');
                                pf.querySelector('.card-input').disabled = false;
                                pf.querySelector('.pay-btn').disabled = false;
                            }
                            break;
                    }
                }
            }

            if (done) break;
        }

        bubble.classList.remove('typing-indicator');
        if (!tracker.querySelector('.wf-step.complete')) {
            finalizeTracker(tracker);
        }
    } catch (err) {
        bubble.textContent = `Error: ${err.message}`;
        bubble.classList.remove('typing-indicator');
    }

    setBusy(false);
    input.focus();
}

// ─── Scripted demo sequence ──────────────────────────────────

const demoBtn = document.getElementById('demo-btn');
if (demoBtn) {
    demoBtn.addEventListener('click', runDemo);
}

async function typeIntoInput(text, msPerChar = 45) {
    input.value = '';
    input.focus();
    for (const ch of text) {
        input.value += ch;
        await new Promise(r => setTimeout(r, msPerChar));
    }
}

async function runDemo() {
    if (busy) return;
    demoBtn.disabled = true;

    // Step 1 — type the initial prompt, then send
    const prompt = 'do you have kid friendly pets';
    await typeIntoInput(prompt);
    await new Promise(r => setTimeout(r, 300));
    input.value = '';
    sendMessage({ message: prompt }, prompt);

    // Step 2 — while the agent is streaming, type the interrupt message and send
    await new Promise(r => setTimeout(r, 1500));
    if (busy) {
        const interrupt = 'also good for granny';
        await typeIntoInput(interrupt);
        await new Promise(r => setTimeout(r, 300));
        input.value = '';
        await doInterrupt(interrupt);
    }

    demoBtn.disabled = false;
}
