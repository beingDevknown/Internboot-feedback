// Code execution functionality
document.addEventListener('DOMContentLoaded', function () {
    console.log('DOM Content Loaded - Initializing code execution');

    // Check if require is defined (loader.js is loaded)
    if (typeof require !== 'undefined') {
        // Initialize Monaco editor
        require.config({ paths: { 'vs': 'https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.44.0/min/vs' } });
        require(['vs/editor/editor.main'], function () {
        console.log('Monaco editor loaded');
        // Create editor instances for each coding question
        document.querySelectorAll('.editor-container').forEach(container => {
            console.log('Creating editor for container');

            // Get the starter code from the container's data attribute
            const starterCode = container.dataset.starterCode || `public class Solution {
    public int addNumbers(int a, int b) {
        return a + b;
    }

    public static void main(String[] args) {
        Solution solution = new Solution();
        // Read input from standard input
        java.util.Scanner scanner = new java.util.Scanner(System.in);
        String input = scanner.nextLine();
        String[] inputs = input.split("[,\\s]+");
        int a = Integer.parseInt(inputs[0].trim());
        int b = Integer.parseInt(inputs[1].trim());
        System.out.println(solution.addNumbers(a, b));
    }
}`;

            const editor = monaco.editor.create(container, {
                value: starterCode,
                language: 'java',
                theme: 'vs-dark',
                automaticLayout: true,
                minimap: { enabled: false }
            });

            // Store editor instance
            container.editor = editor;
            console.log('Editor created and stored');
        });
    });

    // Handle run code button clicks
    document.addEventListener('click', async function (e) {
        const runButton = e.target.closest('.run-code-btn');
        if (runButton) {
            console.log('Run button clicked');
            const button = runButton;
            const container = button.closest('.code-editor-wrapper') || button.parentElement;
            if (!container) {
                console.error('Could not find code editor container');
                return;
            }

            const editorContainer = container.querySelector('.editor-container');
            if (!editorContainer || !editorContainer.editor) {
                console.error('Could not find editor instance');
                return;
            }

            const editor = editorContainer.editor;

            // Find or create result panel
            let resultPanel = document.querySelector('.test-case-result');
            if (!resultPanel) {
                resultPanel = document.createElement('div');
                resultPanel.className = 'test-case-result';
                container.appendChild(resultPanel);
            }

            // Validate inputs
            const code = editor.getValue().trim();
            if (!code) {
                showError(resultPanel, 'Please write some code before running');
                return;
            }

            // Get test cases from the question data
            const questionElement = button.closest('.question-section');
            const testCases = [
                {
                    input: "2, 3",
                    expectedOutput: "5"
                },
                {
                    input: "-10, 7",
                    expectedOutput: "-3"
                }
            ];

            // Show loading state
            showLoading(button, resultPanel);

            try {
                console.log('Sending request to API');
                // Get question ID from the question section
                const questionId = questionElement.dataset.questionId;

                // Call your API
                const response = await fetch('/api/code/run', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({
                        code: code,
                        language: 'java',
                        questionId: questionId,
                        testCases: testCases
                    })
                });

                console.log('API Request body:', {
                    code: code,
                    language: 'java',
                    questionId: questionId,
                    testCases: testCases
                });

                console.log('API Response status:', response.status);

                if (!response.ok) {
                    const errorData = await response.json();
                    throw new Error(errorData.error || 'Failed to execute code');
                }

                const result = await response.json();
                console.log('API Result:', result);

                // Display results
                if (result.success) {
                    showSuccess(resultPanel, result);
                } else {
                    showError(resultPanel, result.compilationError || 'Test failed');
                }
            } catch (error) {
                console.error('Error executing code:', error);
                showError(resultPanel, error.message || 'Failed to execute code');
            } finally {
                // Reset button state
                resetButton(button);
            }
        }
    });

    // Helper functions
    function showLoading(button, resultPanel) {
        if (button) {
            button.disabled = true;
            button.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Running...';
        }
        if (resultPanel) {
            resultPanel.innerHTML = '<div class="loading">Running tests...</div>';
        }
    }

    function resetButton(button) {
        if (button) {
            button.disabled = false;
            button.innerHTML = '<i class="fas fa-play"></i> Run Code';
        }
    }

    function showError(resultPanel, message) {
        if (resultPanel) {
            resultPanel.className = 'test-case-result result-error';
            resultPanel.innerHTML = `
                <div class="error-message">
                    <h5><i class="fas fa-times-circle"></i> Error</h5>
                    <p>${message}</p>
                </div>
            `;
        }
    }

    function showSuccess(resultPanel, result) {
        if (resultPanel) {
            const allPassed = result.testResults.every(test => test.passed);
            resultPanel.className = `test-case-result ${allPassed ? 'result-success' : 'result-error'}`;

            const resultHtml = `
                <div class="test-results">
                    <h5>
                        ${allPassed
                    ? '<i class="fas fa-check-circle"></i> All Tests Passed!'
                    : '<i class="fas fa-times-circle"></i> Some Tests Failed'}
                    </h5>
                    <p>Total Time: ${result.totalExecutionTime}ms</p>
                    <div class="test-cases">
                        ${result.testResults.map((test, index) => `
                            <div class="test-case ${test.passed ? 'passed' : 'failed'}">
                                <div class="test-header">
                                    <strong>Test Case ${index + 1}: ${test.passed ? 'Passed' : 'Failed'}</strong>
                                    <span class="time">${test.executionTime}ms</span>
                                </div>
                                <div class="test-details">
                                    <div class="input">Input: "${test.input}"</div>
                                    <div class="expected">Expected: "${test.expectedOutput}"</div>
                                    <div class="actual">Actual: "${test.actualOutput}"</div>
                                    ${!test.passed ? `<div class="error">${test.error || 'Output does not match expected result'}</div>` : ''}
                                </div>
                            </div>
                        `).join('')}
                    </div>
                </div>
            `;

            resultPanel.innerHTML = resultHtml;
        }
    }
    } // Close the if (typeof require !== 'undefined') block
});