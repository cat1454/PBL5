import React from 'react';
import AnimatedAiLogo from '../common/AnimatedAiLogo';

function AiAssistantCanvas({ hideEyes = false, peekEyes = false, isTyping = false }) {
  return <AnimatedAiLogo size="large" hideEyes={hideEyes} peekEyes={peekEyes} isTyping={isTyping} />;
}

export default AiAssistantCanvas;
