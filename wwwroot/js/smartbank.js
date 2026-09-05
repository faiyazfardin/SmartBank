/**
 * SmartBank Premier Interactive Web App Utilities
 */

document.addEventListener('DOMContentLoaded', () => {
    // 1. Balance Visibility Toggle
    const toggleBalanceBtn = document.getElementById('btnToggleBalance');
    const balanceReal = document.getElementById('valBalanceReal');
    const balanceMasked = document.getElementById('valBalanceMasked');

    if (toggleBalanceBtn && balanceReal && balanceMasked) {
        let isHidden = localStorage.getItem('sb_balance_hidden') === 'true';

        function updateBalanceDisplay() {
            if (isHidden) {
                balanceReal.classList.add('d-none');
                balanceMasked.classList.remove('d-none');
                toggleBalanceBtn.innerHTML = '<i class="bi bi-eye"></i> Show Balance';
            } else {
                balanceReal.classList.remove('d-none');
                balanceMasked.classList.add('d-none');
                toggleBalanceBtn.innerHTML = '<i class="bi bi-eye-slash"></i> Hide Balance';
            }
        }

        updateBalanceDisplay();

        toggleBalanceBtn.addEventListener('click', (e) => {
            e.preventDefault();
            isHidden = !isHidden;
            localStorage.setItem('sb_balance_hidden', isHidden);
            updateBalanceDisplay();
        });
    }

    // 2. Preset Amount Chips Selector
    const presetChips = document.querySelectorAll('.preset-chip');
    const amountInput = document.getElementById('inputCustomAmount');

    if (presetChips.length > 0 && amountInput) {
        presetChips.forEach(chip => {
            chip.addEventListener('click', () => {
                const val = chip.getAttribute('data-value');
                amountInput.value = val;
                
                presetChips.forEach(c => c.classList.remove('active'));
                chip.classList.add('active');
                
                // Trigger change event
                amountInput.dispatchEvent(new Event('input'));
            });
        });

        amountInput.addEventListener('input', () => {
            const currentVal = amountInput.value.trim();
            presetChips.forEach(chip => {
                if (chip.getAttribute('data-value') === currentVal) {
                    chip.classList.add('active');
                } else {
                    chip.classList.remove('active');
                }
            });
        });
    }

    // 3. Password Visibility Toggle
    const togglePasswordButtons = document.querySelectorAll('.toggle-password-btn');
    togglePasswordButtons.forEach(btn => {
        btn.addEventListener('click', () => {
            const targetId = btn.getAttribute('data-target');
            const targetInput = document.getElementById(targetId);
            if (targetInput) {
                if (targetInput.type === 'password') {
                    targetInput.type = 'text';
                    btn.innerHTML = '<i class="bi bi-eye-slash"></i>';
                } else {
                    targetInput.type = 'password';
                    btn.innerHTML = '<i class="bi bi-eye"></i>';
                }
            }
        });
    });

    // 4. Copy to Clipboard Utility
    window.copyToClipboard = function(text, buttonElement) {
        if (navigator.clipboard && window.isSecureContext) {
            navigator.clipboard.writeText(text).then(() => {
                showCopyFeedback(buttonElement);
            });
        } else {
            // Fallback
            const textArea = document.createElement("textarea");
            textArea.value = text;
            textArea.style.position = "fixed";
            textArea.style.left = "-999999px";
            document.body.appendChild(textArea);
            textArea.focus();
            textArea.select();
            try {
                document.execCommand('copy');
                showCopyFeedback(buttonElement);
            } catch (err) {
                console.error('Copy fallback failed', err);
            }
            document.body.removeChild(textArea);
        }
    };

    function showCopyFeedback(btn) {
        if (!btn) return;
        const origHtml = btn.innerHTML;
        btn.innerHTML = '<i class="bi bi-check2"></i> Copied!';
        btn.classList.add('btn-success');
        setTimeout(() => {
            btn.innerHTML = origHtml;
            btn.classList.remove('btn-success');
        }, 2000);
    }

    // 5. Auto dismiss alerts after 5 seconds
    const autoAlerts = document.querySelectorAll('.alert-auto-dismiss');
    autoAlerts.forEach(alert => {
        setTimeout(() => {
            const bsAlert = new bootstrap.Alert(alert);
            bsAlert.close();
        }, 5000);
    });

    // 6. Live Table Search Filter (Client-side instant filter)
    const clientSearchInputs = document.querySelectorAll('.client-table-search');
    clientSearchInputs.forEach(input => {
        const targetTableId = input.getAttribute('data-table');
        const table = document.getElementById(targetTableId);
        if (!table) return;

        input.addEventListener('input', () => {
            const query = input.value.toLowerCase().trim();
            const rows = table.querySelectorAll('tbody tr');
            rows.forEach(row => {
                const text = row.textContent.toLowerCase();
                row.style.display = text.includes(query) ? '' : 'none';
            });
        });
    });
});
