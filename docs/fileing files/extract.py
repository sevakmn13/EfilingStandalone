from bs4 import BeautifulSoup

# Read the HTML file
with open('input.html', 'r', encoding='utf-8') as f:
    html_content = f.read()

# Parse the HTML
soup = BeautifulSoup(html_content, 'html.parser')

# Find all div elements with class 'css-1u8qly9'
values = []
for div in soup.find_all('div', class_='css-1u8qly9'):
    text = div.get_text(strip=True)
    if text:
        values.append(text)

# Print the extracted values
for value in values:
    print(value)