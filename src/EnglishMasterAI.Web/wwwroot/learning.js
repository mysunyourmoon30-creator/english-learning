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

    audioContext: null,
    sourceNode: null,
    processorNode: null,
    silentGain: null,
    pcmChunks: [],
    sampleRate: 0,
    stream: null,
    lastBlob: null,
    recognition: null,
    transcript: "",
    recordingStartedAt: 0,

    async startRecording() {
        const AudioContext = window.AudioContext || window.webkitAudioContext;
        if (!navigator.mediaDevices?.getUserMedia || !AudioContext) {
            throw new Error("Audio recording is not supported by this browser.");
        }
        this.stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        this.pcmChunks = [];
        this.lastBlob = null;
        this.transcript = "";
        this.recordingStartedAt = Date.now();
        this.audioContext = new AudioContext();
        this.sampleRate = this.audioContext.sampleRate;
        this.sourceNode = this.audioContext.createMediaStreamSource(this.stream);
        this.processorNode = this.audioContext.createScriptProcessor(4096, 1, 1);
        this.silentGain = this.audioContext.createGain();
        this.silentGain.gain.value = 0;
        this.processorNode.onaudioprocess = event => {
            this.pcmChunks.push(new Float32Array(event.inputBuffer.getChannelData(0)));
        };
        this.sourceNode.connect(this.processorNode);
        this.processorNode.connect(this.silentGain);
        this.silentGain.connect(this.audioContext.destination);

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
        if (!this.audioContext) {
            return { size: 0, type: "audio/wav", durationSeconds: 0, transcript: "" };
        }
        this.stream?.getTracks().forEach(track => track.stop());
        try { this.recognition?.stop(); } catch { }
        this.sourceNode?.disconnect();
        this.processorNode?.disconnect();
        this.silentGain?.disconnect();
        await this.audioContext.close();

        const blob = this.encodeWav(this.pcmChunks, this.sampleRate);
        this.lastBlob = blob;
        const audio = document.getElementById(audioElementId);
        if (audio) {
            if (audio.dataset.objectUrl) URL.revokeObjectURL(audio.dataset.objectUrl);
            const url = URL.createObjectURL(blob);
            audio.src = url;
            audio.dataset.objectUrl = url;
            audio.hidden = false;
        }

        this.recognition = null;
        this.audioContext = null;
        this.sourceNode = null;
        this.processorNode = null;
        this.silentGain = null;
        this.stream = null;
        this.pcmChunks = [];
        return {
            size: blob.size,
            type: "audio/wav",
            durationSeconds: Math.max(1, Math.round((Date.now() - this.recordingStartedAt) / 1000)),
            transcript: this.transcript.trim()
        };
    },

    getRecordingBlob() {
        if (!this.lastBlob) throw new Error("No recording is available.");
        return this.lastBlob;
    },

    encodeWav(chunks, sampleRate) {
        const sampleCount = chunks.reduce((total, chunk) => total + chunk.length, 0);
        const buffer = new ArrayBuffer(44 + sampleCount * 2);
        const view = new DataView(buffer);
        const writeAscii = (offset, value) => {
            for (let index = 0; index < value.length; index++) {
                view.setUint8(offset + index, value.charCodeAt(index));
            }
        };

        writeAscii(0, "RIFF");
        view.setUint32(4, 36 + sampleCount * 2, true);
        writeAscii(8, "WAVE");
        writeAscii(12, "fmt ");
        view.setUint32(16, 16, true);
        view.setUint16(20, 1, true);
        view.setUint16(22, 1, true);
        view.setUint32(24, sampleRate, true);
        view.setUint32(28, sampleRate * 2, true);
        view.setUint16(32, 2, true);
        view.setUint16(34, 16, true);
        writeAscii(36, "data");
        view.setUint32(40, sampleCount * 2, true);

        let offset = 44;
        for (const chunk of chunks) {
            for (let index = 0; index < chunk.length; index++) {
                const sample = Math.max(-1, Math.min(1, chunk[index]));
                view.setInt16(
                    offset,
                    sample < 0 ? sample * 0x8000 : sample * 0x7fff,
                    true);
                offset += 2;
            }
        }
        return new Blob([view], { type: "audio/wav" });
    }
};
