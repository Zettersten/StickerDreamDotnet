// Microphone access check
window.checkMicrophoneAccess = async () => {
    try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        stream.getTracks().forEach(track => track.stop());
        return true;
    } catch (error) {
        console.error("Microphone access denied:", error);
        return false;
    }
};

// Recording state
let mediaRecorder = null;
let audioChunks = [];
let recordingTimeout = null;
let dotNetRef = null;

// Initialize transcriber (using HuggingFace Transformers.js)
let transcriber = null;

async function initializeTranscriber() {
    if (transcriber) return transcriber;
    
    try {
        const { pipeline } = await import('https://cdn.jsdelivr.net/npm/@xenova/transformers@2.17.2');
        transcriber = await pipeline(
            'automatic-speech-recognition',
            'Xenova/whisper-tiny.en'
        );
        return transcriber;
    } catch (error) {
        console.error('Failed to initialize transcriber:', error);
        throw error;
    }
}

// Start recording
window.startRecording = async (dotNetObjectRef) => {
    dotNetRef = dotNetObjectRef;
    audioChunks = [];
    
    try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        mediaRecorder = new MediaRecorder(stream);
        
        mediaRecorder.ondataavailable = (event) => {
            if (event.data.size > 0) {
                audioChunks.push(event.data);
            }
        };
        
        mediaRecorder.onstop = async () => {
            try {
                const audioBlob = new Blob(audioChunks, { type: 'audio/webm' });
                const audioUrl = URL.createObjectURL(audioBlob);
                
                // Transcribe using Whisper
                const transcriberInstance = await initializeTranscriber();
                const output = await transcriberInstance(audioUrl);
                const text = Array.isArray(output) ? output[0].text : output.text;
                
                // Clean up
                stream.getTracks().forEach(track => track.stop());
                URL.revokeObjectURL(audioUrl);
                
                // Call back to Blazor
                if (dotNetRef) {
                    await dotNetRef.invokeMethodAsync('OnRecordingComplete', audioUrl, text);
                }
            } catch (error) {
                console.error('Error processing recording:', error);
                if (dotNetRef) {
                    await dotNetRef.invokeMethodAsync('OnRecordingComplete', '', 'Error transcribing audio');
                }
            }
        };
        
        mediaRecorder.start();
        
        // Auto-stop after 15 seconds
        recordingTimeout = setTimeout(() => {
            if (mediaRecorder && mediaRecorder.state === 'recording') {
                mediaRecorder.stop();
                mediaRecorder.stream.getTracks().forEach(track => track.stop());
            }
        }, 15000);
    } catch (error) {
        console.error('Error starting recording:', error);
        if (dotNetRef) {
            await dotNetRef.invokeMethodAsync('OnRecordingComplete', '', 'Error starting recording');
        }
    }
};

// Stop recording
window.stopRecording = () => {
    if (recordingTimeout) {
        clearTimeout(recordingTimeout);
        recordingTimeout = null;
    }
    
    if (mediaRecorder && mediaRecorder.state === 'recording') {
        mediaRecorder.stop();
        mediaRecorder.stream.getTracks().forEach(track => track.stop());
    }
};
