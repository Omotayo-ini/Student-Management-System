const root = document.getElementById('voice-assistant');

if (root) {
    const toggle = document.getElementById('voice-assistant-toggle');
    const panel = document.getElementById('voice-assistant-panel');
    const close = document.getElementById('voice-assistant-close');
    const record = document.getElementById('voice-assistant-record');
    const send = document.getElementById('voice-assistant-send');
    const transcript = document.getElementById('voice-assistant-transcript');
    const state = document.getElementById('voice-assistant-state');
    const message = document.getElementById('voice-assistant-message');
    const confirmation = document.getElementById('voice-assistant-confirmation');
    const confirmationText = document.getElementById('voice-assistant-confirmation-text');
    const confirm = document.getElementById('voice-assistant-confirm');
    const cancel = document.getElementById('voice-assistant-cancel');
    const canvas = document.getElementById('voice-assistant-visualizer');
    const canvasContext = canvas.getContext('2d');

    const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    let recognition;
    let audioContext;
    let analyser;
    let microphoneStream;
    let microphoneSource;
    let animationFrame;
    let isListening = false;
    let pendingConfirmation = null;

    function setPanel(open) {
        panel.hidden = !open;
        toggle.setAttribute('aria-expanded', String(open));
    }

    function setState(text, type = '') {
        state.textContent = text;
        state.className = `voice-assistant-state${type ? ` is-${type}` : ''}`;
    }

    function setMessage(text) {
        message.textContent = text;
    }

    function drawIdle() {
        const width = canvas.clientWidth || 320;
        const height = canvas.clientHeight || 72;
        const ratio = window.devicePixelRatio || 1;
        canvas.width = width * ratio;
        canvas.height = height * ratio;
        canvasContext.setTransform(ratio, 0, 0, ratio, 0, 0);
        canvasContext.clearRect(0, 0, width, height);
        canvasContext.strokeStyle = 'rgba(255,255,255,0.28)';
        canvasContext.lineWidth = 2;
        canvasContext.beginPath();
        canvasContext.moveTo(0, height / 2);
        canvasContext.lineTo(width, height / 2);
        canvasContext.stroke();
    }

    function drawVisualizer() {
        if (!analyser) return;
        const width = canvas.clientWidth || 320;
        const height = canvas.clientHeight || 72;
        const ratio = window.devicePixelRatio || 1;
        const data = new Uint8Array(analyser.frequencyBinCount);
        analyser.getByteFrequencyData(data);
        canvas.width = width * ratio;
        canvas.height = height * ratio;
        canvasContext.setTransform(ratio, 0, 0, ratio, 0, 0);
        canvasContext.clearRect(0, 0, width, height);
        const barWidth = width / data.length * 2.2;
        const gradient = canvasContext.createLinearGradient(0, 0, width, 0);
        gradient.addColorStop(0, '#65d83f');
        gradient.addColorStop(0.5, '#b7ee36');
        gradient.addColorStop(1, '#f97316');
        canvasContext.fillStyle = gradient;
        for (let i = 0; i < data.length; i++) {
            const barHeight = Math.max(2, (data[i] / 255) * height * 0.9);
            const x = i * barWidth;
            canvasContext.fillRect(x, (height - barHeight) / 2, Math.max(1, barWidth - 1), barHeight);
        }
        animationFrame = requestAnimationFrame(drawVisualizer);
    }

    async function startAudio() {
        if (!navigator.mediaDevices?.getUserMedia) {
            throw new Error('Microphone access is not supported by this browser.');
        }
        audioContext = new (window.AudioContext || window.webkitAudioContext)();
        microphoneStream = await navigator.mediaDevices.getUserMedia({ audio: true });
        microphoneSource = audioContext.createMediaStreamSource(microphoneStream);
        analyser = audioContext.createAnalyser();
        analyser.fftSize = 128;
        analyser.smoothingTimeConstant = 0.75;
        microphoneSource.connect(analyser);
        drawVisualizer();
    }

    function stopAudio() {
        cancelAnimationFrame(animationFrame);
        microphoneStream?.getTracks().forEach(track => track.stop());
        microphoneSource?.disconnect();
        analyser?.disconnect?.();
        if (audioContext && audioContext.state !== 'closed') audioContext.close();
        audioContext = null;
        microphoneStream = null;
        microphoneSource = null;
        analyser = null;
        drawIdle();
    }

    function createRecognition() {
        if (!SpeechRecognition) return null;
        const instance = new SpeechRecognition();
        instance.continuous = false;
        instance.interimResults = true;
        instance.lang = 'en-US';
        instance.onstart = () => {
            isListening = true;
            root.classList.add('is-listening');
            record.innerHTML = '<i class="bi bi-stop-fill me-1"></i>Stop listening';
            setState('Listening');
            setMessage('Speak clearly. Your words will appear below.');
        };
        instance.onresult = event => {
            let text = '';
            for (let i = event.resultIndex; i < event.results.length; i++) text += event.results[i][0].transcript;
            transcript.value = text.trim();
        };
        instance.onerror = event => {
            setState('Error', 'error');
            setMessage(event.error === 'not-allowed' ? 'Microphone permission was denied.' : `Speech recognition failed: ${event.error}.`);
        };
        instance.onend = () => {
            isListening = false;
            root.classList.remove('is-listening');
            record.innerHTML = '<i class="bi bi-mic-fill me-1"></i>Start listening';
            stopAudio();
            if (transcript.value.trim()) submitCommand(false);
            else setState('Ready');
        };
        return instance;
    }

    async function startListening() {
        if (!SpeechRecognition) {
            setState('Unavailable', 'error');
            setMessage('Speech recognition is unavailable. Type your command instead.');
            transcript.focus();
            return;
        }
        if (isListening) {
            recognition?.stop();
            return;
        }
        try {
            await startAudio();
            recognition = createRecognition();
            recognition.start();
        } catch (error) {
            stopAudio();
            setState('Error', 'error');
            setMessage(error.message || 'Unable to start the microphone.');
        }
    }

    async function submitCommand(confirmed) {
        const text = transcript.value.trim();
        if (!text) {
            setState('Ready');
            setMessage('Speak or type a command first.');
            transcript.focus();
            return;
        }
        setState('Working');
        setMessage('Processing your command...');
        send.disabled = true;
        confirm.disabled = true;
        try {
            const response = await fetch('/api/voice-commands', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                body: JSON.stringify({ transcript: text, confirm: confirmed })
            });
            const result = await response.json();
            if (!response.ok) throw new Error(result.detail || 'The command could not be processed.');
            handleResponse(result);
        } catch (error) {
            setState('Error', 'error');
            setMessage(error.message || 'Unable to contact the voice command service.');
        } finally {
            send.disabled = false;
            confirm.disabled = false;
        }
    }

    function handleResponse(result) {
        confirmation.hidden = true;
        if (result.status === 'needs_clarification') {
            setState('Needs details');
            setMessage(result.message || `Missing: ${(result.missingFields || []).join(', ')}`);
            transcript.focus();
            return;
        }
        if (result.status === 'confirmation_required') {
            pendingConfirmation = result;
            confirmationText.textContent = result.message;
            confirmation.hidden = false;
            setState('Confirm action');
            setMessage('Review the request before it changes the records.');
            return;
        }
        if (result.status === 'client_action' && result.clientAction === 'export-analytics-pdf') {
            const url = result.parameters?.url || '/Analytics';
            setState('Opening analytics');
            setMessage('Opening analytics and preparing the PDF export...');
            window.location.href = `${url}?voiceExport=1`;
            return;
        }
        if (result.status === 'executed') {
            setState('Complete', 'success');
            setMessage(result.message || 'Command completed successfully.');
            transcript.value = '';
            return;
        }
        setState('Not recognized', 'error');
        setMessage(result.message || 'I could not identify that command.');
    }

    toggle.addEventListener('click', () => {
        setPanel(panel.hidden);
        if (!panel.hidden) transcript.focus();
    });
    close.addEventListener('click', () => {
        if (isListening) recognition?.stop();
        setPanel(false);
    });
    record.addEventListener('click', startListening);
    send.addEventListener('click', () => submitCommand(false));
    confirm.addEventListener('click', () => {
        if (pendingConfirmation) submitCommand(true);
    });
    cancel.addEventListener('click', () => {
        pendingConfirmation = null;
        confirmation.hidden = true;
        setState('Ready');
        setMessage('Action cancelled.');
    });
    transcript.addEventListener('keydown', event => {
        if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') submitCommand(false);
    });
    window.addEventListener('resize', drawIdle);
    window.addEventListener('pagehide', () => {
        recognition?.abort();
        stopAudio();
    }, { once: true });
    drawIdle();
}
