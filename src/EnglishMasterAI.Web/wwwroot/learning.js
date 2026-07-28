window.englishMaster = {
    speak(text, rate) {
        if (!("speechSynthesis" in window)) return;
        window.speechSynthesis.cancel();
        const utterance = new SpeechSynthesisUtterance(text);
        utterance.lang = "en-US";
        utterance.rate = Number(rate) || 1;
        const voices = window.speechSynthesis.getVoices();
        utterance.voice = voices.find(v => v.lang?.startsWith("en") && /Natural|Samantha|Google|Microsoft/i.test(v.name))
            || voices.find(v => v.lang?.startsWith("en"))
            || null;
        window.speechSynthesis.speak(utterance);
    },

    recorder: null,
    chunks: [],
    stream: null,
    lastBlob: null,
    recognition: null,
    transcript: "",
    recordingStartedAt: 0,

    async startRecording() {
        if (!navigator.mediaDevices?.getUserMedia || !window.MediaRecorder) {
            throw new Error("Audio recording is not supported by this browser.");
        }
        this.stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        this.chunks = [];
        this.lastBlob = null;
        this.transcript = "";
        this.recordingStartedAt = Date.now();
        this.recorder = new MediaRecorder(this.stream);
        this.recorder.ondataavailable = event => {
            if (event.data.size > 0) this.chunks.push(event.data);
        };
        this.recorder.start();

        const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (Recognition) {
            try {
                this.recognition = new Recognition();
                this.recognition.lang = "en-US";
                this.recognition.continuous = true;
                this.recognition.interimResults = false;
                this.recognition.onresult = event => {
                    for (let index = event.resultIndex; index < event.results.length; index++) {
                        if (event.results[index].isFinal) {
                            this.transcript += `${event.results[index][0].transcript} `;
                        }
                    }
                };
                this.recognition.onerror = () => { };
                this.recognition.start();
            } catch {
                this.recognition = null;
            }
        }
        return true;
    },

    async stopRecording(audioElementId) {
        if (!this.recorder) {
            return { size: 0, type: "audio/webm", durationSeconds: 0, transcript: "" };
        }
        const recorder = this.recorder;
        return new Promise(resolve => {
            recorder.onstop = () => {
                const blob = new Blob(this.chunks, { type: recorder.mimeType || "audio/webm" });
                this.lastBlob = blob;
                const audio = document.getElementById(audioElementId);
                if (audio) {
                    if (audio.dataset.objectUrl) URL.revokeObjectURL(audio.dataset.objectUrl);
                    const url = URL.createObjectURL(blob);
                    audio.src = url;
                    audio.dataset.objectUrl = url;
                    audio.hidden = false;
                }
                this.stream?.getTracks().forEach(track => track.stop());
                try { this.recognition?.stop(); } catch { }
                this.recognition = null;
                this.recorder = null;
                this.stream = null;
                resolve({
                    size: blob.size,
                    type: blob.type || "audio/webm",
                    durationSeconds: Math.max(1, Math.round((Date.now() - this.recordingStartedAt) / 1000)),
                    transcript: this.transcript.trim()
                });
            };
            recorder.stop();
        });
    },

    getRecordingBlob() {
        if (!this.lastBlob) throw new Error("No recording is available.");
        return this.lastBlob;
    }
};
