import React, { useEffect, useRef } from 'react';

function AnimatedAiLogo({ size = 'large', hideEyes = false, peekEyes = false, isTyping = false, className = '' }) {
  const canvasRef = useRef(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const W = 180;
    const H = 180;

    // Track mouse globally across the whole page
    const mouse = { x: W / 2, y: H / 2 };
    let lastActiveTime = Date.now();
    let isAsleep = false;

    const handleMouseMove = (e) => {
      const rect = canvas.getBoundingClientRect();
      const canvasCenterX = rect.left + rect.width / 2;
      const canvasCenterY = rect.top + rect.height / 2;
      const dx = e.clientX - canvasCenterX;
      const dy = e.clientY - canvasCenterY;
      const dist = Math.sqrt(dx * dx + dy * dy) || 1;
      const maxTravel = 90;
      const factor = Math.min(dist, maxTravel) / dist;
      mouse.x = W / 2 + dx * factor;
      mouse.y = H / 2 + dy * factor;

      lastActiveTime = Date.now();
      if (isAsleep) {
        isAsleep = false;
      }
    };

    window.addEventListener('mousemove', handleMouseMove);

    // Track local hover state inside the canvas
    let isHovered = false;
    const handleMouseEnter = () => {
      isHovered = true;
      lastActiveTime = Date.now();
      if (isAsleep) isAsleep = false;
    };
    const handleMouseLeave = () => {
      isHovered = false;
    };

    canvas.addEventListener('mouseenter', handleMouseEnter);
    canvas.addEventListener('mouseleave', handleMouseLeave);

    // Click interactive states
    let clickSpinT = 0;
    let dizzyT = 0;
    const particles = [];

    const handleCanvasClick = () => {
      clickSpinT = 30; // 30 frames of spin
      dizzyT = 90; // 90 frames of dizzy state
      lastActiveTime = Date.now();
      if (isAsleep) isAsleep = false;
      
      // Spawn explosive burst of stars and hearts
      for (let i = 0; i < 12; i++) {
        const angle = (i / 12) * Math.PI * 2 + (Math.random() - 0.5) * 0.3;
        const speed = 2.0 + Math.random() * 2.5;
        particles.push({
          x: W / 2,
          y: H / 2 - 30,
          vx: Math.cos(angle) * speed,
          vy: Math.sin(angle) * speed - 1.0,
          alpha: 1.0,
          scale: 0.6 + Math.random() * 0.8,
          char: Math.random() < 0.4 ? '⭐' : (Math.random() < 0.5 ? '✨' : '♥')
        });
      }
    };

    canvas.addEventListener('click', handleCanvasClick);

    // Physics & animation states
    const eyeCurL = { x: 0, y: 0, vx: 0, vy: 0 };
    const eyeCurR = { x: 0, y: 0, vx: 0, vy: 0 };
    let blinkT = 0;
    let blinkNext = 120;
    let blinkDur = 0;
    let isBlinking = false;
    let antennaT = 0;
    let bodyBobT = 0;
    let earWiggleT = 0;
    let ledT = 0;
    
    let shieldAlpha = 0;
    let peekAlpha = 0;
    let typingAlpha = 0;
    let sleepAlpha = 0;

    const lerp = (a, b, t) => a + (b - a) * t;

    const getEyeTarget = (ex, ey) => {
      const dx = mouse.x - ex;
      const dy = mouse.y - ey;
      const dist = Math.sqrt(dx * dx + dy * dy) || 1;

      // Restrict active gaze tracking distance for small logos in navbar to prevent distraction
      const maxActiveDist = size === 'large' ? 99999 : 250;
      if (dist > maxActiveDist) {
        return { x: 0, y: 0 };
      }

      const maxR = 4.5;
      const fadeFactor = 1 - (dist / maxActiveDist);
      const r = Math.min(dist, maxR * 8) / (maxR * 8);
      return { 
        x: (dx / dist) * r * maxR * fadeFactor, 
        y: (dy / dist) * r * maxR * fadeFactor * 0.8 
      };
    };

    const drawRR = (x, y, w, h, r, fill, stroke, lw) => {
      ctx.beginPath();
      ctx.moveTo(x + r, y);
      ctx.lineTo(x + w - r, y);
      ctx.quadraticCurveTo(x + w, y, x + w, y + r);
      ctx.lineTo(x + w, y + h - r);
      ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
      ctx.lineTo(x + r, y + h);
      ctx.quadraticCurveTo(x, y + h, x, y + h - r);
      ctx.lineTo(x, y + r);
      ctx.quadraticCurveTo(x, y, x + r, y);
      ctx.closePath();
      if (fill) {
        ctx.fillStyle = fill;
        ctx.fill();
      }
      if (stroke) {
        ctx.strokeStyle = stroke;
        ctx.lineWidth = lw || 1.5;
        ctx.stroke();
      }
    };

    const drawMittenHand = (mx, my, armAngle, isLeft) => {
      ctx.save();
      ctx.translate(mx, my);
      ctx.rotate(armAngle);

      ctx.fillStyle = '#5dd4b4';
      ctx.strokeStyle = '#1d7a7a';
      ctx.lineWidth = 1.5;

      // Mitten palm
      ctx.beginPath();
      ctx.arc(0, 0, 5.5, 0, Math.PI * 2);
      ctx.fill();
      ctx.stroke();

      // Thumb
      ctx.beginPath();
      const thumbAngle = isLeft ? -Math.PI / 3 : Math.PI / 3;
      const tx = Math.cos(thumbAngle) * 4.5;
      const ty = Math.sin(thumbAngle) * 4.5;
      ctx.arc(tx, ty, 2.5, 0, Math.PI * 2);
      ctx.fill();
      ctx.stroke();

      // Internal separation line
      ctx.beginPath();
      ctx.moveTo(0, -5.5);
      ctx.lineTo(0, 0);
      ctx.strokeStyle = '#1d7a7a';
      ctx.lineWidth = 0.8;
      ctx.stroke();

      ctx.restore();
    };

    // Check prefers-reduced-motion
    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    let animationId = null;

    const draw = () => {
      ctx.clearRect(0, 0, W, H);

      // Lerping values for smooth state transitions
      const shieldTarget = hideEyes ? 1 : 0;
      shieldAlpha = lerp(shieldAlpha, shieldTarget, 0.08);

      const peekTarget = peekEyes ? 1 : 0;
      peekAlpha = lerp(peekAlpha, peekTarget, 0.08);

      const typingTarget = isTyping ? 1 : 0;
      typingAlpha = lerp(typingAlpha, typingTarget, 0.08);

      // Sleep check
      const timeSinceActive = Date.now() - lastActiveTime;
      const IDLE_THRESHOLD = 8000; // 8 seconds of absolute inactivity
      if (timeSinceActive > IDLE_THRESHOLD && !isHovered && !hideEyes && !peekEyes && !isTyping) {
        isAsleep = true;
      } else {
        isAsleep = false;
      }

      const sleepTarget = isAsleep ? 1 : 0;
      sleepAlpha = lerp(sleepAlpha, sleepTarget, 0.06);

      // 1. Spawning particles (hearts, sparkles, blossoms, and sleep bubbles 'z')
      if (!prefersReducedMotion) {
        // Hover particles
        if (isHovered && Math.random() < 0.12) {
          particles.push({
            x: W / 2 + (Math.random() - 0.5) * 44,
            y: H / 2 - 45,
            vx: (Math.random() - 0.5) * 1.2,
            vy: -0.8 - Math.random() * 1.5,
            alpha: 1.0,
            scale: 0.5 + Math.random() * 0.5,
            char: Math.random() < 0.4 ? '♥' : (Math.random() < 0.5 ? '✨' : '🌸')
          });
        }
        // Sleep Z's particles
        if (isAsleep && Math.random() < 0.02) {
          particles.push({
            x: W / 2 + (Math.random() - 0.5) * 10,
            y: H / 2 - 76,
            vx: 0.2 + Math.random() * 0.4,
            vy: -0.4 - Math.random() * 0.4,
            alpha: 1.0,
            scale: 0.6 + Math.random() * 0.4,
            char: Math.random() < 0.5 ? 'z' : 'Z'
          });
        }
      }

      // 2. Setup Spin Matrix if clicked
      ctx.save();
      const cx = W / 2;
      let bodyBob = 0;
      let antWave = 0;
      let earWig = 0;

      if (!prefersReducedMotion) {
        // Speed up animation cycles when hovered/dizzy, slow down when sleeping
        const speedMultiplier = dizzyT > 0 ? 2.5 : isHovered ? 2.2 : isAsleep ? 0.35 : 1.0;
        antennaT += 0.038 * speedMultiplier;
        bodyBobT += 0.022 * speedMultiplier;
        earWiggleT += 0.034 * speedMultiplier;
        ledT += 0.02 * speedMultiplier;

        bodyBob = Math.sin(bodyBobT) * (isAsleep ? 1.0 : isHovered ? 3.5 : 2.0) + Math.sin(bodyBobT * 1.7) * 0.4;
        antWave = Math.sin(antennaT) * (isAsleep ? 1.5 : isHovered ? 6.0 : 3.5) + Math.sin(antennaT * 2.2) * 0.9;
        earWig = Math.sin(earWiggleT) * (isAsleep ? 0.5 : isHovered ? 4.5 : 1.8);
      }

      const cy = H / 2 + 25 + bodyBob;

      if (clickSpinT > 0) {
        clickSpinT--;
        const spinAngle = ((30 - clickSpinT) / 30) * Math.PI * 2;
        ctx.translate(cx, cy);
        ctx.rotate(spinAngle);
        ctx.translate(-cx, -cy);
      }

      const bodyX = cx - 38;
      const bodyY = cy - 28;
      const bodyW = 76;
      const bodyH = 64;
      
      const headX = cx - 30;
      const headY = cy - 70;
      const headW = 60;
      const headH = 52;

      const eyeLX = cx - 16;
      const eyeRX = cx + 16;
      const eyeY = headY + 22;
      const eyeR = 9;

      // Calculate arm coordinates based on state transitions
      // Left Arm
      let leftArmEndX = bodyX - 9;
      let leftArmEndY = bodyY + 36;
      if (isHovered) {
        leftArmEndX = bodyX - 12 - Math.cos(antennaT * 4.0) * 4;
        leftArmEndY = bodyY + 6 + Math.sin(antennaT * 4.0) * 12;
      }
      if (dizzyT > 0) {
        leftArmEndX = bodyX - 14;
        leftArmEndY = bodyY - 4 + Math.sin(antennaT * 6.0) * 4;
      }
      // Apply typing state lerping
      if (typingAlpha > 0.01) {
        const typeSpeed = antennaT * 15;
        const typingX = cx - 18 + Math.sin(typeSpeed) * 2;
        const typingY = bodyY + 32 + Math.cos(typeSpeed * 1.5) * 3;
        leftArmEndX = lerp(leftArmEndX, typingX, typingAlpha);
        leftArmEndY = lerp(leftArmEndY, typingY, typingAlpha);
      }
      // Apply shield state lerping (password hidden)
      if (shieldAlpha > 0.01) {
        leftArmEndX = lerp(leftArmEndX, eyeLX - 1, shieldAlpha);
        leftArmEndY = lerp(leftArmEndY, eyeY + 2, shieldAlpha);
      }
      // Apply peek state lerping (password visible)
      if (peekAlpha > 0.01) {
        leftArmEndX = lerp(leftArmEndX, eyeLX - 10, peekAlpha);
        leftArmEndY = lerp(leftArmEndY, eyeY + 4, peekAlpha);
      }

      // Right Arm
      let rightArmEndX = bodyX + bodyW + 9;
      let rightArmEndY = bodyY + 36;
      if (dizzyT > 0) {
        rightArmEndX = bodyX + bodyW + 14;
        rightArmEndY = bodyY - 4 + Math.cos(antennaT * 6.0) * 4;
      }
      // Apply typing state lerping
      if (typingAlpha > 0.01) {
        const typeSpeed = antennaT * 15;
        const typingX = cx + 18 + Math.cos(typeSpeed) * 2;
        const typingY = bodyY + 32 + Math.sin(typeSpeed * 1.5) * 3;
        rightArmEndX = lerp(rightArmEndX, typingX, typingAlpha);
        rightArmEndY = lerp(rightArmEndY, typingY, typingAlpha);
      }
      // Apply shield state lerping (password hidden)
      if (shieldAlpha > 0.01) {
        rightArmEndX = lerp(rightArmEndX, eyeRX + 1, shieldAlpha);
        rightArmEndY = lerp(rightArmEndY, eyeY + 2, shieldAlpha);
      }
      // Apply peek state lerping (password visible)
      if (peekAlpha > 0.01) {
        rightArmEndX = lerp(rightArmEndX, eyeRX + 10, peekAlpha);
        rightArmEndY = lerp(rightArmEndY, eyeY + 4, peekAlpha);
      }

      // Legs
      drawRR(bodyX + 4, bodyY + 42, 18, 28, 6, '#1a5a5a', null);
      drawRR(bodyX + bodyW - 22, bodyY + 42, 18, 28, 6, '#1a5a5a', null);

      // Antenna
      ctx.beginPath();
      ctx.moveTo(cx, headY);
      ctx.lineTo(cx + antWave, headY - 22);
      ctx.strokeStyle = '#5dd4b4';
      ctx.lineWidth = 2.5;
      ctx.lineCap = 'round';
      ctx.stroke();

      ctx.beginPath();
      ctx.arc(cx + antWave, headY - 26, 5, 0, Math.PI * 2);
      ctx.fillStyle = '#5dd4b4';
      ctx.fill();

      // Ears
      const earLX = headX - 10;
      const earRX = headX + headW - 4;
      const earY = headY + 12;
      drawRR(earLX, earY + earWig * 0.5, 12, 22, 4, '#1a5a5a', null);
      drawRR(earLX + 2, earY + 3 + earWig * 0.5, 8, 14, 3, '#5dd4b4', null);
      drawRR(earRX, earY - earWig * 0.5, 12, 22, 4, '#1a5a5a', null);
      drawRR(earRX + 2, earY + 3 - earWig * 0.5, 8, 14, 3, '#5dd4b4', null);

      // Head
      drawRR(headX, headY, headW, headH, 14, '#1d7a7a', '#5dd4b4', 1.5);
      
      // Sweat drop when dizzy
      if (dizzyT > 0) {
        ctx.save();
        ctx.fillStyle = '#38bdf8';
        ctx.beginPath();
        const sx = headX + 7;
        const sy = headY + 12;
        ctx.moveTo(sx, sy);
        ctx.quadraticCurveTo(sx - 4, sy + 6, sx, sy + 9);
        ctx.quadraticCurveTo(sx + 4, sy + 6, sx, sy);
        ctx.fill();
        ctx.restore();
      }

      // Body
      drawRR(bodyX, bodyY, bodyW, bodyH, 10, '#1d7a7a', null);

      // AI badge
      ctx.fillStyle = 'rgba(255,255,255,0.08)';
      ctx.beginPath();
      ctx.arc(cx, bodyY + 15, 20, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#5dd4b4';
      ctx.font = '500 13px monospace';
      ctx.textAlign = 'center';
      ctx.fillText('AI', cx, bodyY + 20);

      // LEDs — smooth sine pulse
      const ledColors = ['#5dd4b4', '#4bbfff', '#c084fc'];
      for (let li = 0; li < 3; li++) {
        let p = 1.0;
        if (!prefersReducedMotion) {
          p = 0.55 + 0.45 * Math.sin(ledT * 1.6 + li * 1.8);
          p = p * p;
        }
        ctx.globalAlpha = p;
        ctx.fillStyle = ledColors[li];
        ctx.beginPath();
        ctx.arc(cx - 14 + li * 14, bodyY + 38, 4, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.globalAlpha = 1;

      // Eyes
      let blinkScale = 1;
      if (!prefersReducedMotion && !isAsleep) {
        blinkT++;
        if (!isBlinking && blinkT >= blinkNext) {
          isBlinking = true;
          blinkDur = 0;
          blinkNext = 80 + Math.random() * 120;
          blinkT = 0;
        }
        if (isBlinking) {
          blinkDur++;
          if (blinkDur > 12) isBlinking = false;
        }
        blinkScale = isBlinking ? Math.max(0.05, 1 - Math.sin((blinkDur / 12) * Math.PI)) : 1;
      }

      if (dizzyT > 0) {
        dizzyT--;
      }

      const blushPulse = prefersReducedMotion ? 1.0 : 1.0 + Math.sin(antennaT * 3.0) * 0.12;

      // Draw eyes depending on current animation state (Sleeping, Covered, Peeking, Dizzy, Hover, Normal)
      if (sleepAlpha > 0.3) {
        // SLEEP STATE EYES: Closed curved lines "u u"
        ctx.save();
        ctx.globalAlpha = Math.min(sleepAlpha, 1);
        [eyeLX, eyeRX].forEach((ex) => {
          ctx.strokeStyle = '#5dd4b4';
          ctx.lineWidth = 2.5;
          ctx.lineCap = 'round';
          ctx.beginPath();
          // Cute curved line
          ctx.arc(ex, eyeY - 2, 5, 0.1 * Math.PI, 0.9 * Math.PI);
          ctx.stroke();

          // Sleep blush cheeks
          ctx.fillStyle = 'rgba(252, 165, 165, 0.2)';
          ctx.beginPath();
          ctx.ellipse(ex, eyeY + 11, 4.5 * blushPulse, 1.8 * blushPulse, 0, 0, Math.PI * 2);
          ctx.fill();
        });
        ctx.restore();
      } else if (shieldAlpha > 0.3) {
        // COVERED STATE EYES (Password Hidden)
        ctx.save();
        ctx.globalAlpha = Math.min(shieldAlpha, 1);
        ctx.fillStyle = '#1d7a7a';
        [eyeLX, eyeRX].forEach((ex) => {
          ctx.beginPath();
          ctx.ellipse(ex, eyeY, eyeR, eyeR * blinkScale, 0, 0, Math.PI * 2);
          ctx.fill();
          ctx.strokeStyle = '#5dd4b4';
          ctx.lineWidth = 1.5;
          ctx.stroke();
        });
        ctx.fillStyle = '#5dd4b4';
        ctx.font = '500 11px monospace';
        ctx.textAlign = 'center';
        ctx.fillText('* *', cx, eyeY + 4);
        ctx.restore();
      } else if (peekAlpha > 0.3) {
        // PEAKING STATE EYES (Password Shown): Left wink, right open looking at user
        ctx.save();
        ctx.globalAlpha = Math.min(peekAlpha, 1);

        // Left Eye (Wink ^)
        ctx.strokeStyle = '#ffffff';
        ctx.lineWidth = 3.0;
        ctx.lineCap = 'round';
        ctx.beginPath();
        ctx.arc(eyeLX, eyeY + 4, eyeR - 2.5, Math.PI * 1.1, Math.PI * 1.9);
        ctx.stroke();

        // Right Eye (Open Wide O)
        ctx.fillStyle = '#ffffff';
        ctx.beginPath();
        ctx.arc(eyeRX, eyeY, eyeR, 0, Math.PI * 2);
        ctx.fill();

        ctx.fillStyle = '#0a3a3a';
        ctx.beginPath();
        ctx.arc(eyeRX - 1.5, eyeY + 1, 4.2, 0, Math.PI * 2);
        ctx.fill();

        ctx.fillStyle = '#ffffff';
        ctx.beginPath();
        ctx.arc(eyeRX - 0.5, eyeY - 0.5, 1.4, 0, Math.PI * 2);
        ctx.fill();

        // Extra deep blushing cheeks when peeking/shy
        [eyeLX, eyeRX].forEach((ex) => {
          ctx.fillStyle = 'rgba(239, 68, 68, 0.6)';
          ctx.beginPath();
          ctx.ellipse(ex, eyeY + 11, 5.5 * blushPulse, 2.5 * blushPulse, 0, 0, Math.PI * 2);
          ctx.fill();
        });
        ctx.restore();
      } else if (dizzyT > 0) {
        // DIZZY STATE EYES: Spiral `@ @`
        [eyeLX, eyeRX].forEach((ex) => {
          ctx.save();
          ctx.fillStyle = '#ffffff';
          ctx.beginPath();
          ctx.arc(ex, eyeY, eyeR, 0, Math.PI * 2);
          ctx.fill();

          ctx.fillStyle = '#0a3a3a';
          ctx.font = 'bold 13px sans-serif';
          ctx.textAlign = 'center';
          ctx.textBaseline = 'middle';
          ctx.fillText('@', ex, eyeY);

          ctx.fillStyle = 'rgba(239, 68, 68, 0.7)';
          ctx.beginPath();
          ctx.ellipse(ex, eyeY + 11, 6 * blushPulse, 2.5 * blushPulse, 0, 0, Math.PI * 2);
          ctx.fill();
          ctx.restore();
        });
      } else if (isHovered) {
        // HOVER STATE EYES: Happy curved shapes `^ ^`
        [eyeLX, eyeRX].forEach((ex) => {
          ctx.save();
          ctx.strokeStyle = '#ffffff';
          ctx.lineWidth = 3.5;
          ctx.lineCap = 'round';
          ctx.beginPath();
          ctx.arc(ex, eyeY + 4, eyeR - 1.5, Math.PI * 1.15, Math.PI * 1.85);
          ctx.stroke();

          ctx.fillStyle = 'rgba(252, 165, 165, 0.7)';
          ctx.beginPath();
          ctx.ellipse(ex, eyeY + 11, 5.5 * blushPulse, 2.4 * blushPulse, 0, 0, Math.PI * 2);
          ctx.fill();
          ctx.restore();
        });
      } else {
        // NORMAL EYE TRACKING STATE
        [eyeLX, eyeRX].forEach((ex, i) => {
          const cur = i === 0 ? eyeCurL : eyeCurR;
          
          let tgt;
          if (typingAlpha > 0.2) {
            // If typing on keyboard, force eyes to look down at it
            tgt = { x: 0, y: 3.5 };
          } else {
            tgt = getEyeTarget(ex, eyeY);
          }

          if (shieldAlpha > 0.01 || peekAlpha > 0.01) {
            tgt = { x: 0, y: 0 };
          }

          if (prefersReducedMotion) {
            cur.x = tgt.x;
            cur.y = tgt.y;
          } else {
            cur.vx = cur.vx * 0.78 + (tgt.x - cur.x) * 0.16;
            cur.vy = cur.vy * 0.78 + (tgt.y - cur.y) * 0.16;
            cur.x += cur.vx;
            cur.y += cur.vy;
          }

          ctx.save();
          ctx.globalAlpha = 1 - Math.max(shieldAlpha, peekAlpha) * 0.8;
          
          ctx.fillStyle = '#ffffff';
          ctx.save();
          ctx.translate(ex, eyeY);
          ctx.scale(1, blinkScale);
          ctx.beginPath();
          ctx.arc(0, 0, eyeR, 0, Math.PI * 2);
          ctx.restore();
          ctx.fill();

          ctx.fillStyle = '#0a3a3a';
          ctx.beginPath();
          ctx.arc(ex + cur.x, eyeY + cur.y * blinkScale, 4.5, 0, Math.PI * 2);
          ctx.fill();

          ctx.fillStyle = '#ffffff';
          ctx.beginPath();
          ctx.arc(ex + cur.x + 1.5, eyeY + cur.y * blinkScale - 1.5, 1.5, 0, Math.PI * 2);
          ctx.fill();

          // Normal pink cheeks blush
          ctx.fillStyle = 'rgba(252, 165, 165, 0.22)';
          ctx.beginPath();
          ctx.ellipse(ex, eyeY + 11, 5, 2.0, 0, 0, Math.PI * 2);
          ctx.fill();

          ctx.restore();
        });
      }

      // Mouth
      const mouthY = headY + 41;
      ctx.beginPath();
      if (shieldAlpha > 0.5) {
        // flat line when covering eyes
        ctx.moveTo(cx - 7, mouthY);
        ctx.lineTo(cx + 7, mouthY);
        ctx.strokeStyle = '#5dd4b4';
        ctx.lineWidth = 2;
        ctx.lineCap = 'round';
        ctx.stroke();

        ctx.save();
        ctx.globalAlpha = shieldAlpha * 0.9;
        ctx.strokeStyle = '#5dd4b4';
        ctx.lineWidth = 2;
        const sx = cx - 10;
        const sy = mouthY - 2;
        ctx.beginPath();
        ctx.moveTo(sx, sy);
        ctx.lineTo(sx + 6, sy);
        ctx.lineTo(sx + 9, sy - 3);
        ctx.lineTo(sx + 13, sy);
        ctx.lineTo(cx + 10, sy);
        ctx.stroke();
        ctx.restore();
      } else if (peekAlpha > 0.5) {
        // Shy wavy smile when peeking
        ctx.strokeStyle = '#5dd4b4';
        ctx.lineWidth = 2;
        ctx.lineCap = 'round';
        ctx.beginPath();
        ctx.arc(cx, mouthY - 3, 6, 0, Math.PI);
        ctx.stroke();
      } else if (dizzyT > 0) {
        // Dizzy wavy mouth
        ctx.strokeStyle = '#5dd4b4';
        ctx.lineWidth = 2;
        ctx.lineCap = 'round';
        ctx.beginPath();
        const sx = cx - 8;
        const sy = mouthY - 1;
        ctx.moveTo(sx, sy);
        ctx.bezierCurveTo(sx + 3, sy - 3, sx + 5, sy + 3, cx, sy);
        ctx.bezierCurveTo(cx + 3, sy - 3, cx + 5, sy + 3, cx + 8, sy);
        ctx.stroke();
      } else if (isHovered) {
        // Happy open smile
        ctx.arc(cx, mouthY - 5, 7, 0, Math.PI);
        ctx.fillStyle = '#5dd4b4';
        ctx.fill();
      } else if (isAsleep) {
        // Tiny relaxed sleeping mouth "o"
        ctx.arc(cx, mouthY - 2, 2.5, 0, Math.PI * 2);
        ctx.strokeStyle = '#5dd4b4';
        ctx.lineWidth = 2;
        ctx.stroke();
      } else {
        // Normal smile
        ctx.arc(cx, mouthY - 3, 8, 0.15, Math.PI - 0.15);
        ctx.strokeStyle = '#5dd4b4';
        ctx.lineWidth = 2;
        ctx.lineCap = 'round';
        ctx.stroke();
      }

      // Draw Holographic neon typing keyboard in front of body
      if (typingAlpha > 0.01) {
        ctx.save();
        ctx.globalAlpha = typingAlpha;

        const kx = cx - 28;
        const ky = bodyY + 28;
        const kw = 56;
        const kh = 16;
        const kr = 4;

        // Neon Glow
        ctx.shadowColor = 'rgba(93, 212, 180, 0.8)';
        ctx.shadowBlur = 8;

        drawRR(kx, ky, kw, kh, kr, 'rgba(29, 122, 122, 0.22)', '#5dd4b4', 1.5);

        ctx.shadowBlur = 0;

        // Key rows / lines
        ctx.strokeStyle = 'rgba(93, 212, 180, 0.5)';
        ctx.lineWidth = 1.0;
        ctx.beginPath();
        ctx.moveTo(kx + 4, ky + 5);
        ctx.lineTo(kx + kw - 4, ky + 5);
        ctx.moveTo(kx + 4, ky + 10);
        ctx.lineTo(kx + kw - 4, ky + 10);
        ctx.stroke();

        // Glowing virtual key hits
        ctx.fillStyle = '#5dd4b4';
        for (let i = 0; i < 4; i++) {
          ctx.beginPath();
          ctx.arc(kx + 8 + i * 13 + Math.sin(antennaT * 8 + i) * 1.5, ky + 5, 1.2, 0, Math.PI * 2);
          ctx.fill();
          ctx.beginPath();
          ctx.arc(kx + 14 + i * 11 + Math.cos(antennaT * 8 + i) * 1.5, ky + 10, 1.2, 0, Math.PI * 2);
          ctx.fill();
        }

        ctx.restore();
      }

      // Draw Arms and mitten hands in front of body/face
      ctx.strokeStyle = '#1d7a7a';
      ctx.lineWidth = 5.5;
      ctx.lineCap = 'round';
      ctx.lineJoin = 'round';

      // Left Arm Line
      ctx.beginPath();
      ctx.moveTo(bodyX + 3, bodyY + 16);
      ctx.lineTo(leftArmEndX, leftArmEndY);
      ctx.stroke();

      // Right Arm Line
      ctx.beginPath();
      ctx.moveTo(bodyX + bodyW - 3, bodyY + 16);
      ctx.lineTo(rightArmEndX, rightArmEndY);
      ctx.stroke();

      // Draw Left Hand
      const leftArmAngle = Math.atan2(leftArmEndY - (bodyY + 16), leftArmEndX - (bodyX + 3));
      drawMittenHand(leftArmEndX, leftArmEndY, leftArmAngle, true);

      // Draw Right Hand
      const rightArmAngle = Math.atan2(rightArmEndY - (bodyY + 16), rightArmEndX - (bodyX + bodyW - 3));
      drawMittenHand(rightArmEndX, rightArmEndY, rightArmAngle, false);

      ctx.restore();

      // 3. Render and update floating particles (stars, hearts, sleep bubbles 'z')
      if (!prefersReducedMotion && particles.length > 0) {
        ctx.save();
        for (let pi = particles.length - 1; pi >= 0; pi--) {
          const p = particles[pi];
          p.x += p.vx;
          p.y += p.vy;
          p.alpha -= p.char.toLowerCase() === 'z' ? 0.009 : 0.016; // sleep bubbles fade slower
          
          if (p.alpha <= 0) {
            particles.splice(pi, 1);
            continue;
          }
          ctx.globalAlpha = p.alpha;
          
          if (p.char.toLowerCase() === 'z') {
            // Sleep bubble 'Zzz' formatting
            ctx.font = `bold ${Math.floor(10 * p.scale)}px monospace`;
            ctx.fillStyle = '#5dd4b4';
            ctx.shadowColor = 'rgba(93, 212, 180, 0.4)';
            ctx.shadowBlur = 4;
            // Float sway motion
            p.x += Math.sin(antennaT + p.y * 0.05) * 0.25;
          } else {
            ctx.font = `${Math.floor(13 * p.scale)}px sans-serif`;
            ctx.fillStyle = p.char === '♥' ? '#ff527b' : (p.char === '✨' ? '#ffd43b' : p.char === '🌸' ? '#ffa6c9' : '#ffb700');
          }
          
          ctx.textAlign = 'center';
          ctx.fillText(p.char, p.x, p.y);
        }
        ctx.restore();
      }

      animationId = requestAnimationFrame(draw);
    };

    draw();

    return () => {
      window.removeEventListener('mousemove', handleMouseMove);
      canvas.removeEventListener('mouseenter', handleMouseEnter);
      canvas.removeEventListener('mouseleave', handleMouseLeave);
      canvas.removeEventListener('click', handleCanvasClick);
      if (animationId) {
        cancelAnimationFrame(animationId);
      }
    };
  }, [hideEyes, peekEyes, isTyping, size]);

  const sizeClass = size === 'large' ? 'size-large' : 'size-small';

  return (
    <canvas
      className={`animated-ai-logo ${sizeClass} ${className}`}
      ref={canvasRef}
      width={180}
      height={180}
      style={{ display: 'block', maxWidth: '100%', cursor: 'pointer' }}
      aria-label="Interactive AI Assistant Mascot illustration"
    />
  );
}

export default AnimatedAiLogo;
