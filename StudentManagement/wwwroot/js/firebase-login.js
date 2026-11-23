import { initializeApp } from 'https://www.gstatic.com/firebasejs/11.0.2/firebase-app.js';
import { browserSessionPersistence, getAuth, setPersistence, signInWithEmailAndPassword } from 'https://www.gstatic.com/firebasejs/11.0.2/firebase-auth.js';

const config = window.eduManagerFirebaseConfig;
let auth;

try {
    if (!config?.apiKey || !config?.authDomain || !config?.projectId || !config?.appId) {
        throw new Error('Firebase web configuration is incomplete.');
    }
    auth = getAuth(initializeApp(config));
} catch (initializationError) {
    const initializationMessage = document.getElementById('login-error');
    if (initializationMessage) {
        initializationMessage.textContent = initializationError.message;
    }
    throw initializationError;
}

const stage = document.querySelector('.login-stage');
const card = document.getElementById('login-card');
const form = document.getElementById('firebase-login-form');
const email = document.getElementById('login-email');
const password = document.getElementById('login-password');
const remember = document.getElementById('login-remember');
const submit = document.getElementById('login-submit');
const error = document.getElementById('login-error');
const togglePassword = document.getElementById('login-password-toggle');
const verificationTitle = document.getElementById('login-verification-title');
const verificationMessage = document.getElementById('login-verification-message');
const particleHost = document.getElementById('login-success-particles');
const transition = document.getElementById('login-transition');

function showError(text) {
    error.textContent = text;
}

function createSuccessParticles() {
    const colors = ['#4de1ff', '#b13bff', '#ffffff', '#63ffb2'];
    particleHost.replaceChildren();
    for (let index = 0; index < 40; index += 1) {
        const particle = document.createElement('span');
        const angle = (Math.PI * 2 * index) / 40 + (Math.random() - .5) * .35;
        const distance = 72 + Math.random() * 55;
        particle.style.setProperty('--particle-x', `${Math.cos(angle) * distance}px`);
        particle.style.setProperty('--particle-y', `${Math.sin(angle) * distance}px`);
        particle.style.setProperty('--particle-color', colors[index % colors.length]);
        particle.style.animationDelay = `${Math.random() * 120}ms`;
        particleHost.appendChild(particle);
    }
}

function playSuccessAnimation() {
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    createSuccessParticles();
    card.classList.add('is-success');
    verificationTitle.textContent = 'Identity confirmed';
    verificationMessage.textContent = 'Welcome back to EduManager.';

    if (reducedMotion) {
        stage.classList.add('is-exiting');
        window.setTimeout(() => window.location.assign('/'), 250);
        return;
    }

    window.setTimeout(() => card.classList.add('is-celebrating'), 650);
    window.setTimeout(() => {
        transition.classList.add('is-expanding');
        stage.classList.add('is-exiting');
    }, 1350);
    window.setTimeout(() => window.location.assign('/'), 1850);
}

function firebaseErrorMessage(firebaseError) {
    switch (firebaseError?.code) {
        case 'auth/invalid-credential':
        case 'auth/invalid-login-credentials':
        case 'auth/user-not-found':
        case 'auth/wrong-password':
            return 'Invalid email or password.';
        case 'auth/too-many-requests':
            return 'Too many attempts. Please wait and try again.';
        case 'auth/network-request-failed':
            return 'Network error. Check your connection and try again.';
        default:
            return 'Unable to sign in. Please try again.';
    }
}

function setTilt(cardElement, stageElement) {
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const updatePointer = event => {
        const bounds = cardElement.getBoundingClientRect();
        const x = event.clientX - bounds.left;
        const y = event.clientY - bounds.top;
        cardElement.style.setProperty('--login-mx', `${x}px`);
        cardElement.style.setProperty('--login-my', `${y}px`);
        cardElement.style.setProperty('--login-hover', '1');
        if (!reducedMotion && event.pointerType === 'mouse') {
            cardElement.style.setProperty('--login-rx', `${(-(y / bounds.height - .5) * 12).toFixed(2)}deg`);
            cardElement.style.setProperty('--login-ry', `${((x / bounds.width - .5) * 14).toFixed(2)}deg`);
        }
    };
    cardElement.addEventListener('pointerenter', updatePointer);
    cardElement.addEventListener('pointermove', updatePointer);
    cardElement.addEventListener('pointerleave', () => {
        cardElement.style.setProperty('--login-hover', '0');
        cardElement.style.setProperty('--login-rx', '0deg');
        cardElement.style.setProperty('--login-ry', '0deg');
    });
}

setTilt(card, stage);

togglePassword.addEventListener('click', () => {
    const visible = password.type === 'password';
    password.type = visible ? 'text' : 'password';
    togglePassword.textContent = visible ? 'Hide' : 'Show';
});

form.addEventListener('submit', async event => {
    event.preventDefault();
    showError('');
    if (!email.value.trim() || !password.value) {
        showError('Enter your email and password.');
        return;
    }

    submit.disabled = true;
    card.classList.add('is-loading');

    try {
        await setPersistence(auth, browserSessionPersistence);
        const credential = await signInWithEmailAndPassword(auth, email.value.trim(), password.value);
        const idToken = await credential.user.getIdToken(true);
        const response = await fetch('/api/auth/session', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify({ idToken, rememberMe: remember.checked })
        });

        if (!response.ok) {
            let serverMessage = 'The server could not establish your session.';
            try {
                const serverResult = await response.json();
                serverMessage = serverResult.message || serverResult.title || serverMessage;
            } catch {
                // Keep the fallback message when the response is not JSON.
            }
            throw new Error(serverMessage);
        }

        playSuccessAnimation();
    } catch (firebaseError) {
        card.classList.remove('is-loading');
        submit.disabled = false;
        showError(firebaseError.message?.startsWith('The server') || firebaseError.message?.startsWith('Firebase')
            ? firebaseError.message
            : firebaseErrorMessage(firebaseError));
    }
});
