
const isWindows = process.platform === 'win32';

(isWindows ? describe : describe.skip)('WinForms GUI Tests', () => {
  let dotnet: any;
  let System: any;
  let Forms: any;
  let Drawing: any;

  beforeAll(async () => {
    try {
      dotnet = await import('../src/index.js');
      dotnet.default.load('System.Windows.Forms');
      dotnet.default.load('System.Drawing');
      System = dotnet.System;
      Forms = System.Windows.Forms;
      Drawing = System.Drawing;
    } catch (e) {
      console.log('Skipping WinForms tests - load failed:', e);
    }
  });

  afterAll(() => {
    try {
      dotnet?.node_ps1_dotnet?._close();
    } catch {}
  });

  it('should load System.Windows.Forms assembly', () => {
    expect(Forms).toBeDefined();
    expect(Forms.Form).toBeDefined();
  });

  it('should load System.Drawing assembly', () => {
    expect(Drawing).toBeDefined();
    expect(Drawing.Color).toBeDefined();
    expect(Drawing.Font).toBeDefined();
    expect(Drawing.Point).toBeDefined();
  });

  it('should create a Form instance', () => {
    const form = new Forms.Form();
    expect(form).toBeDefined();
    expect(form.__ref).toBeDefined();
  });

  it('should set Form properties', () => {
    const form = new Forms.Form();
    form.Text = 'Test Form';
    form.Width = 800;
    form.Height = 600;
    form.StartPosition = 1;
    
    expect(form.Text).toBe('Test Form');
    expect(form.Width).toBe(800);
    expect(form.Height).toBe(600);
  });

  it('should create a Label control', () => {
    const label = new Forms.Label();
    expect(label).toBeDefined();
    
    label.Text = 'Test Label';
    label.Font = new Drawing.Font('Arial', 14);
    label.AutoSize = true;
    
    expect(label.Text).toBe('Test Label');
    expect(label.AutoSize).toBe(true);
  });

  it('should create a Button control', () => {
    const button = new Forms.Button();
    expect(button).toBeDefined();
    
    button.Text = 'Click Me';
    button.AutoSize = true;
    
    expect(button.Text).toBe('Click Me');
  });

  it('should create a Panel control', () => {
    const panel = new Forms.Panel();
    expect(panel).toBeDefined();
    
    panel.Width = 100;
    panel.Height = 100;
    panel.BackColor = Drawing.Color.Red;
    
    expect(panel.Width).toBe(100);
    expect(panel.Height).toBe(100);
  });

  it('should create a Point for location', () => {
    const point = new Drawing.Point(100, 200);
    expect(point).toBeDefined();
    expect(point.X).toBe(100);
    expect(point.Y).toBe(200);
  });

  it('should create a Font', () => {
    const font = new Drawing.Font('Arial', 12);
    expect(font).toBeDefined();
    expect(font.Name).toContain('Arial');
  });

  it('should add controls to form', () => {
    const form = new Forms.Form();
    const label = new Forms.Label();
    const button = new Forms.Button();
    
    form.Controls.Add(label);
    form.Controls.Add(button);
    
    expect(form.Controls.Count).toBeGreaterThanOrEqual(2);
  });

  it('should register click event handler', () => {
    const button = new Forms.Button();
    let clicked = false;
    
    button.add_Click(() => {
      clicked = true;
    });
    
    expect(button).toBeDefined();
  });

  it('should create Color constants', () => {
    expect(Drawing.Color.Red).toBeDefined();
    expect(Drawing.Color.Green).toBeDefined();
    expect(Drawing.Color.Blue).toBeDefined();
    expect(Drawing.Color.Black).toBeDefined();
    expect(Drawing.Color.White).toBeDefined();
  });
});
